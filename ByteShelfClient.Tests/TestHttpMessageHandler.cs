using System.Net;
using System.Text;

namespace ByteShelfClient.Tests
{
    /// <summary>
    /// Test HTTP message handler for mocking HTTP responses in unit tests.
    /// </summary>
    public class TestHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();
        private readonly Dictionary<string, (string Content, HttpStatusCode StatusCode)> _responses = new Dictionary<string, (string, HttpStatusCode)>();

        /// <summary>
        /// Sets up a response for a specific URL pattern.
        /// </summary>
        /// <param name="url">The URL pattern to match (supports wildcards with *)</param>
        /// <param name="content">The response content</param>
        /// <param name="statusCode">The HTTP status code (defaults to 200 OK)</param>
        public void SetupResponse(string url, string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responses[url] = (content, statusCode);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            string url = request.RequestUri!.PathAndQuery;

            // Find matching response
            foreach (KeyValuePair<string, (string Content, HttpStatusCode StatusCode)> response in _responses)
            {
                if (url.Contains(response.Key.Replace("*", "")))
                {
                    HttpResponseMessage httpResponse = new HttpResponseMessage(response.Value.StatusCode)
                    {
                        Content = new StringContent(response.Value.Content, Encoding.UTF8, "application/json")
                    };
                    return Task.FromResult(httpResponse);
                }
            }

            // Default 404 response
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}