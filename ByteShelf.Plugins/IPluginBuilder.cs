using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Plugins
{
    /// <summary>Takes what a plugin adds to the server, which knows the plugin by it.</summary>
    public interface IPluginBuilder
    {
        /// <summary>
        /// The plugin's own copy of the server's services, to add its own to. A typed client
        /// (<c>AddHttpClient&lt;T&gt;</c>) needs a class name no other plugin's has.
        /// </summary>
        IServiceCollection Services { get; }
    }
}
