namespace ByteShelfClient.Http
{
    /// <summary>
    /// How <see cref="ByteShelfHttpClient"/> proves who it is: a <see cref="ShelfKeyCredentials"/> for a
    /// program, or a <see cref="UserSession"/> for a signed-in user.
    /// </summary>
    public abstract class ByteShelfCredentials
    {
        private protected ByteShelfCredentials()
        {
        }

        /// <summary>Adds the credentials to <paramref name="request"/>, renewing them first when they are about to run out.</summary>
        internal abstract Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken);

        /// <summary>
        /// Called once when the server answered <paramref name="rejected"/> with a challenged 401. True
        /// when the credentials have been renewed since, so sending the request again may succeed.
        /// </summary>
        internal virtual Task<bool> RenewAfterRejectionAsync(HttpRequestMessage rejected, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }
    }
}
