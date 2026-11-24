using System.Net;
using ByteShelf.Server.Shared.Auth;
using ByteShelfCommon;

namespace ByteShelf.Server.Shared.Errors
{
    /// <summary>
    /// The single place where domain exceptions map to an HTTP status and an <see cref="ErrorResponse"/>:
    /// the API answers with it (<see cref="Middleware.ExceptionHandlingMiddleware"/>), a batch answers each
    /// refused item with it, and the server's commands tell the same. Anything not listed here is a
    /// fault: 500, with its message kept from the caller.
    /// </summary>
    internal static class Refusals
    {
        /// <summary>
        /// What the caller is told of <paramref name="exception"/> when it is a refusal it can act on, as
        /// the API answers it; null for a fault, which the API answers with 500.
        /// </summary>
        public static ErrorResponse? Of(Exception exception)
        {
            (HttpStatusCode statusCode, string message, string? code) = Classify(exception);
            return statusCode == HttpStatusCode.InternalServerError ? null : new ErrorResponse { Message = message, Code = code };
        }

        /// <summary>The status, message and code the API answers <paramref name="exception"/> with.</summary>
        public static (HttpStatusCode StatusCode, string Message, string? Code) Classify(Exception exception)
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
                RetryLaterException => (HttpStatusCode.ServiceUnavailable, exception.Message, ErrorCodes.NotReady),
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
