using System.Net;
using System.Text.Json;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Shared.Json;
using ByteShelfCommon;

namespace ByteShelf.Server.Shared.Middleware
{
    /// <summary>
    /// The single place where domain exceptions map to HTTP status codes and an
    /// <see cref="ErrorResponse"/> body. Domains throw; controllers stay free of try/catch. Anything
    /// not listed here is a fault: 500, logged, and its message kept out of the response.
    /// </summary>
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception exception) when (context.RequestAborted.IsCancellationRequested)
            {
                // The client went away. Whatever the request was doing then failed for that reason
                // (a cancelled read, a reset connection), and there is nobody left to answer.
                _logger.LogDebug(exception, "Request {Path} was aborted by the client.", context.Request.Path);
            }
            catch (Exception exception)
            {
                await HandleExceptionAsync(context, exception);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            if (context.Response.HasStarted)
            {
                // A streaming response already sent headers — writing a JSON error body now would
                // corrupt the stream and throw, masking the original exception. Log it and let the
                // connection tear down as-is.
                _logger.LogWarning(exception, "Exception after the response started; unable to write an error body.");
                return;
            }

            (HttpStatusCode statusCode, string message, string? code) = Classify(exception);

            if (statusCode == HttpStatusCode.InternalServerError)
            {
                _logger.LogError(exception, "Unhandled exception occurred");
            }
            else
            {
                _logger.LogInformation("Request {Path} answered {StatusCode}: {Message}", context.Request.Path, (int)statusCode, message);
            }

            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";

            ErrorResponse body = new ErrorResponse { Message = message, Code = code };
            await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonDefaults.CamelCase));
        }

        private static (HttpStatusCode StatusCode, string Message, string? Code) Classify(Exception exception)
        {
            return exception switch
            {
                AuthenticatedUserException => (HttpStatusCode.Unauthorized, exception.Message, ErrorCodes.Unauthenticated),
                NotFoundException => (HttpStatusCode.NotFound, exception.Message, ErrorCodes.NotFound),
                PermissionDeniedException => (HttpStatusCode.Forbidden, exception.Message, ErrorCodes.PermissionDenied),
                ConflictException => (HttpStatusCode.Conflict, exception.Message, ErrorCodes.Conflict),
                ReadOnlyReplicaException => (HttpStatusCode.Conflict, exception.Message, ErrorCodes.ReadOnlyReplica),
                PrimaryFailedException => (HttpStatusCode.BadGateway, exception.Message, ErrorCodes.PrimaryFailed),
                QuotaExceededException => (HttpStatusCode.InsufficientStorage, exception.Message, ErrorCodes.QuotaExceeded),
                LengthRequiredException => (HttpStatusCode.LengthRequired, exception.Message, ErrorCodes.InvalidRequest),

                // Kestrel's refusal of a malformed request or a body over the endpoint's limit. It
                // derives from IOException, so it has to be recognised before it reaches the 500 arm.
                BadHttpRequestException badRequest => (
                    (HttpStatusCode)badRequest.StatusCode,
                    badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge ? "The request body is larger than this endpoint accepts." : "The request is malformed.",
                    badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge ? ErrorCodes.TooLarge : ErrorCodes.InvalidRequest),

                ArgumentException argumentException => (HttpStatusCode.BadRequest, MessageWithoutParameterName(argumentException), ErrorCodes.InvalidRequest),
                _ => (HttpStatusCode.InternalServerError, "An error occurred while processing your request.", null),
            };
        }

        /// <summary>
        /// The message a caller sees for invalid input: <see cref="ArgumentException.Message"/> appends
        /// "(Parameter 'x')" when a parameter name is set, which names server internals.
        /// </summary>
        private static string MessageWithoutParameterName(ArgumentException exception)
        {
            string suffix = $" (Parameter '{exception.ParamName}')";
            return exception.ParamName != null && exception.Message.EndsWith(suffix, StringComparison.Ordinal)
                ? exception.Message[..^suffix.Length]
                : exception.Message;
        }
    }
}
