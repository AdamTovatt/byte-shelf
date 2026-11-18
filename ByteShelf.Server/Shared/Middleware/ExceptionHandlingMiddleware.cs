using System.Net;
using System.Text.Json;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Shared.Json;
using ByteShelfCommon;

namespace ByteShelf.Server.Shared.Middleware
{
    /// <summary>
    /// Answers an exception with the status and <see cref="ErrorResponse"/> body
    /// <see cref="Refusals"/> maps it to. Domains throw; controllers stay free of try/catch. A fault
    /// (500) is logged, and its message kept out of the response.
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

            (HttpStatusCode statusCode, string message, string? code) = Refusals.Classify(exception);

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
    }
}
