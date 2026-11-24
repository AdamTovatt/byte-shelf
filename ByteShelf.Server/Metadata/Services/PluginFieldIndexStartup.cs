namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>Runs <see cref="PluginFieldIndex"/> as the server starts; the start waits for it.</summary>
    internal sealed class PluginFieldIndexStartup : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public PluginFieldIndexStartup(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PluginFieldIndex>().BringUpToDateAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
