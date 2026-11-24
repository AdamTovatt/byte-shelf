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
        /// Adds what reads files' contents for the plugin, one at most. It is made with no services, the first
        /// time it has something to read, and may be made and run in another process than the rest of the plugin,
        /// with which it shares no state. One whose constructor throws, or takes over 60 seconds, is not run,
        /// which admins see; <c>--check-plugin</c> does not make it, and does not tell.
        /// </summary>
        /// <param name="version">The reader's version: what another version read, or failed to read, is read again.</param>
        /// <param name="mediaTypes">The media types it takes, lowercase: <c>image/png</c>, or a family as <c>image/*</c>.</param>
        /// <exception cref="InvalidOperationException">The plugin has one already.</exception>
        /// <exception cref="ArgumentException">One of the types is no media type or family of them.</exception>
        void AddReader<TReader>(int version, IReadOnlyList<string> mediaTypes)
            where TReader : class, IContentDocumentReader, new();
    }
}
