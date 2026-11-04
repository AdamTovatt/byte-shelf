using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;

namespace ByteShelfClient.Http
{
    /// <summary>Sends requests to the server with the caller's credentials, and turns failures into exceptions.</summary>
    internal sealed class ApiConnection
    {
        private readonly HttpClient _http;
        private readonly ByteShelfCredentials _credentials;

        public ApiConnection(HttpClient http, ByteShelfCredentials credentials)
        {
            _http = http;
            _credentials = credentials;
        }

        /// <summary>The server's address, ending in <c>/</c>, which every request is relative to.</summary>
        public Uri BaseAddress => _http.BaseAddress!;

        /// <summary>
        /// Sends the request <paramref name="createRequest"/> builds and answers the successful response,
        /// which the caller disposes. When the server challenges the credentials and they can be renewed,
        /// the request is built and sent once more, so build its content anew each time; a streamed body
        /// that cannot be read twice passes <paramref name="canRetry"/> false.
        /// </summary>
        /// <exception cref="ByteShelfException">The server answered with an error.</exception>
        public async Task<HttpResponseMessage> SendRawAsync(
            Func<HttpRequestMessage> createRequest,
            CancellationToken cancellationToken,
            bool canRetry = true,
            HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
        {
            (HttpRequestMessage request, HttpResponseMessage response) = await SendOnceAsync(createRequest, completion, cancellationToken).ConfigureAwait(false);
            try
            {
                // Only a challenge (WWW-Authenticate) means the token itself was refused; a 401 without one
                // is an answer about something else, such as a wrong current password.
                if (response.StatusCode == HttpStatusCode.Unauthorized
                    && response.Headers.WwwAuthenticate.Count > 0
                    && canRetry
                    && await _credentials.RenewAfterRejectionAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    response.Dispose();
                    (_, response) = await SendOnceAsync(createRequest, completion, cancellationToken).ConfigureAwait(false);
                }

                await ApiErrors.ThrowIfFailedAsync(response, cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        public async Task<T> SendAsync<T>(HttpMethod method, string uri, JsonTypeInfo<T> resultType, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await SendRawAsync(() => new HttpRequestMessage(method, uri), cancellationToken).ConfigureAwait(false);
            return await ReadAsync(response, resultType, cancellationToken).ConfigureAwait(false);
        }

        public async Task<T> SendAsync<TBody, T>(HttpMethod method, string uri, TBody body, JsonTypeInfo<TBody> bodyType, JsonTypeInfo<T> resultType, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await SendRawAsync(() => WithJson(method, uri, body, bodyType), cancellationToken).ConfigureAwait(false);
            return await ReadAsync(response, resultType, cancellationToken).ConfigureAwait(false);
        }

        public async Task SendAsync(HttpMethod method, string uri, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await SendRawAsync(() => new HttpRequestMessage(method, uri), cancellationToken).ConfigureAwait(false);
        }

        public async Task SendAsync<TBody>(HttpMethod method, string uri, TBody body, JsonTypeInfo<TBody> bodyType, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await SendRawAsync(() => WithJson(method, uri, body, bodyType), cancellationToken).ConfigureAwait(false);
        }

        private static HttpRequestMessage WithJson<TBody>(HttpMethod method, string uri, TBody body, JsonTypeInfo<TBody> bodyType)
        {
            return new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body, bodyType) };
        }

        /// <summary>Reads the JSON body of a successful <paramref name="response"/>.</summary>
        /// <exception cref="ByteShelfException">It has none.</exception>
        public static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> resultType, CancellationToken cancellationToken)
        {
            return await response.Content.ReadFromJsonAsync(resultType, cancellationToken).ConfigureAwait(false)
                ?? throw new ByteShelfException($"The server answered {(int)response.StatusCode} without a body.", code: null, response.StatusCode);
        }

        private async Task<(HttpRequestMessage Request, HttpResponseMessage Response)> SendOnceAsync(
            Func<HttpRequestMessage> createRequest,
            HttpCompletionOption completion,
            CancellationToken cancellationToken)
        {
            HttpRequestMessage request = createRequest();
            await _credentials.ApplyAsync(request, cancellationToken).ConfigureAwait(false);
            return (request, await _http.SendAsync(request, completion, cancellationToken).ConfigureAwait(false));
        }
    }
}
