namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>The runs under way, each over some contents, so that a removal of the contents finds and stops them.</summary>
    internal sealed class PluginClaims
    {
        private readonly Lock _lock = new Lock();
        private readonly HashSet<Claim> _claims = new HashSet<Claim>();

        /// <summary>Notes a run over the contents, which <paramref name="stop"/> stops; end it once the run is over.</summary>
        /// <exception cref="OperationCanceledException">It was stopped already.</exception>
        public Claim Add(string sha256, CancellationTokenSource stop)
        {
            lock (_lock)
            {
                stop.Token.ThrowIfCancellationRequested();
                Claim claim = new Claim(this, sha256, stop);
                _claims.Add(claim);
                return claim;
            }
        }

        /// <summary>Stops every run over the contents, and waits for each to be over.</summary>
        public Task StopAsync(string sha256)
        {
            List<Claim> claims;
            lock (_lock)
            {
                claims = _claims.Where(claim => claim.Sha256 == sha256).ToList();
            }

            return StopAsync(claims);
        }

        /// <summary>Stops every run, and waits for each to be over.</summary>
        public Task StopAllAsync()
        {
            List<Claim> claims;
            lock (_lock)
            {
                claims = _claims.ToList();
            }

            return StopAsync(claims);
        }

        private static async Task StopAsync(IReadOnlyList<Claim> claims)
        {
            foreach (Claim claim in claims)
            {
                try
                {
                    await claim.Stop.CancelAsync();
                }
                catch (ObjectDisposedException)
                {
                    // It ended just now.
                }
            }

            foreach (Claim claim in claims)
            {
                await claim.Ended.Task;
            }
        }

        /// <summary>A run under way over some contents.</summary>
        internal sealed class Claim
        {
            private readonly PluginClaims _claims;

            public Claim(PluginClaims claims, string sha256, CancellationTokenSource stop)
            {
                _claims = claims;
                Sha256 = sha256;
                Stop = stop;
            }

            public string Sha256 { get; }

            public CancellationTokenSource Stop { get; }

            public TaskCompletionSource Ended { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Has the run be over, for whoever waits for it to stop.</summary>
            public void End()
            {
                lock (_claims._lock)
                {
                    _claims._claims.Remove(this);
                }

                Ended.SetResult();
            }
        }
    }
}
