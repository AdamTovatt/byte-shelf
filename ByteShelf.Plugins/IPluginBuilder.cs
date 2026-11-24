using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Plugins
{
    /// <summary>Takes what a plugin adds to the server.</summary>
    public interface IPluginBuilder
    {
        /// <summary>
        /// The plugin's own copy of the server's services, to add its own to. A typed client
        /// (<c>AddHttpClient&lt;T&gt;</c>) needs a class name no other plugin's has.
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// Adds what reads files' contents for the plugin, one at most, as a singleton made from
        /// <see cref="Services"/> when the server starts, so it may take no scoped service. One that cannot be
        /// made, as its constructor throws or needs a service that is not there, is logged and reads nothing
        /// until the server restarts; <c>--check-plugin</c> does not make it, and does not tell.
        /// </summary>
        /// <param name="version">The reader's version: what another version read, or failed to read, is read again.</param>
        /// <param name="mediaTypes">The media types it takes, lowercase: <c>image/png</c>, or a family as <c>image/*</c>.</param>
        /// <exception cref="InvalidOperationException">The plugin has one already.</exception>
        /// <exception cref="ArgumentException">One of the types is no media type or family of them.</exception>
        void AddReader<TReader>(int version, IReadOnlyList<string> mediaTypes)
            where TReader : class, IContentDocumentReader;
    }
}
