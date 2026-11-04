using System.Net;
using System.Text;

namespace ByteShelf.Cli.Tests.TestUtilities
{
    /// <summary>
    /// A stand-in for servers, answering from a script and recording each request as
    /// "METHOD url credential", the credential being the bearer token or the shelf key it carried.
    /// </summary>
    internal sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;
        private readonly List<string> _requests = new List<string>();

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> answer)
        {
            _answer = answer;
        }

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

        public static HttpResponseMessage Json(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string credential = request.Headers.Authorization?.Parameter
                ?? (request.Headers.TryGetValues(ByteShelfCommon.ShelfKeys.HeaderName, out IEnumerable<string>? keys) ? keys.Single() : "none");
            lock (_requests)
            {
                _requests.Add($"{request.Method} {request.RequestUri} {credential}");
            }

            return Task.FromResult(_answer(request));
        }
    }
}
