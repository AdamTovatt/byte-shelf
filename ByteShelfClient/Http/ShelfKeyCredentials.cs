using ByteShelfCommon;

namespace ByteShelfClient.Http
{
    /// <summary>A shelf key's secret, sent on every request. A key works on its one shelf only.</summary>
    public sealed class ShelfKeyCredentials : ByteShelfCredentials
    {
        private readonly string _secret;

        /// <param name="secret">The secret shown once when the key was created, starting <c>bsk_</c>.</param>
        public ShelfKeyCredentials(string secret)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(secret);
            _secret = secret;
        }

        internal override Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add(ShelfKeys.HeaderName, _secret);
            return Task.CompletedTask;
        }
    }
}
