using System.Net;
using System.Text;

namespace ByteShelfClient.Tests
{
    /// <summary>
    /// A stand-in server answering from a script, for what the real one cannot be made to do on cue:
    /// refuse a valid token, or fail one chunk of an upload. Records every request it receives.
    /// </summary>
    internal sealed class ScriptedServer : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _answer;
        private readonly List<string> _requests = new List<string>();

        public ScriptedServer(Func<HttpRequestMessage, HttpResponseMessage> answer)
            : this(request => Task.FromResult(answer(request)))
        {
        }

        /// <summary>A server whose answers can wait, to hold a request in flight while another is sent.</summary>
        public ScriptedServer(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer)
        {
            _answer = answer;
        }

        /// <summary>Each request as "METHOD /path?query", with the bearer token it carried after a space when it had one.</summary>
        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToList();
                }
            }
        }

        public HttpClient CreateClient()
        {
            return new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("http://shelf.test/") };
        }

        public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        /// <summary>A sign-in or refresh answer; the access token runs out in an hour unless <paramref name="expiresAt"/> says otherwise.</summary>
        public static string Tokens(string token, string refreshToken, DateTimeOffset? expiresAt = null)
        {
            return $"{{\"token\":\"{token}\",\"expiresAt\":\"{expiresAt ?? DateTimeOffset.UtcNow.AddHours(1):O}\",\"refreshToken\":\"{refreshToken}\"}}";
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string bearer = request.Headers.Authorization?.Parameter is { } token ? " " + token : string.Empty;
            lock (_requests)
            {
                _requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery}{bearer}");
            }

            return _answer(request);
        }
    }
}
