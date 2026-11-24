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
        /// Adds what reads files' contents for the plugin, one at most. It is made with no services and run in a
        /// child process of the server's, which has loaded the plugin anew and shares no state with the rest of it.
        /// One whose constructor throws is not run, which admins see. One that takes over 60 seconds, or crashes, keeps
        /// the process from loading, and so the plugin's maker from running too. <c>--check-plugin</c> does not
        /// make it, and does not tell.
        /// </summary>
        /// <param name="version">The reader's version: what another version read, or failed to read, is read again.</param>
        /// <param name="mediaTypes">The media types it takes, lowercase: <c>image/png</c>, or a family as <c>image/*</c>.</param>
        /// <exception cref="InvalidOperationException">The plugin has one already.</exception>
        /// <exception cref="ArgumentException">One of the types is no media type or family of them.</exception>
        void AddReader<TReader>(int version, IReadOnlyList<string> mediaTypes)
            where TReader : class, IContentDocumentReader, new();

        /// <summary>
        /// Adds what makes derived files for the plugin, one at most, made and run as a reader is
        /// (<see cref="AddReader{TReader}"/>), in the same process. That one could not run is in the server's log
        /// alone.
        /// </summary>
        /// <param name="makes">What it makes, each kind once. Of two plugins making one kind from a type, one that names it exactly goes before one that names its family, then the first by id.</param>
        /// <exception cref="InvalidOperationException">The plugin has one already.</exception>
        /// <exception cref="ArgumentException">A kind is there twice, or one of the types is no media type or family of them.</exception>
        void AddMaker<TMaker>(params IReadOnlyList<DerivedFileDeclaration> makes)
            where TMaker : class, IDerivedFileMaker, new();
    }
}
