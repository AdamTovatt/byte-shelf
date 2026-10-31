using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ByteShelfCommon;
using ByteShelfCommon.Serialization;

namespace ByteShelfClient.Http
{
    /// <summary>Turns a failed response into a <see cref="ByteShelfException"/>.</summary>
    internal static class ApiErrors
    {
        /// <summary>Throws the server's error unless <paramref name="response"/> succeeded.</summary>
        /// <exception cref="ByteShelfException">It did not.</exception>
        public static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            ErrorResponse? body = null;
            try
            {
                body = await response.Content.ReadFromJsonAsync(ByteShelfJsonContext.Default.ErrorResponse, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
            }
            catch (NotSupportedException)
            {
                // Not JSON at all, such as the empty 401 the authentication middleware answers.
            }

            string message = body?.Message ?? $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.";
            throw new ByteShelfException(message, body?.Code ?? CodeFor(response.StatusCode), response.StatusCode);
        }

        /// <summary>The code an error without one in its body stands for, judged by its status.</summary>
        private static string? CodeFor(HttpStatusCode status)
        {
            return status switch
            {
                HttpStatusCode.BadRequest => ErrorCodes.InvalidRequest,
                HttpStatusCode.Unauthorized => ErrorCodes.Unauthenticated,
                HttpStatusCode.Forbidden => ErrorCodes.PermissionDenied,
                HttpStatusCode.NotFound => ErrorCodes.NotFound,
                HttpStatusCode.Conflict => ErrorCodes.Conflict,
                HttpStatusCode.RequestEntityTooLarge => ErrorCodes.TooLarge,
                HttpStatusCode.InsufficientStorage => ErrorCodes.QuotaExceeded,
                _ => null,
            };
        }
    }
}
