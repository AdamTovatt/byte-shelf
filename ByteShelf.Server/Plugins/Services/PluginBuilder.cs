using ByteShelf.Plugins;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>What one plugin registers through.</summary>
    internal sealed record PluginBuilder(string Id, IServiceCollection Services) : IPluginBuilder;
}
