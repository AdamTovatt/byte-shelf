namespace ByteShelfClient
{
    /// <summary>What a plugin made from a file's contents, as <see cref="IShelfClient.OpenDerivedAsync"/> opens it. Disposing it disposes <see cref="Content"/>.</summary>
    public sealed class DerivedFileContent : IDisposable, IAsyncDisposable
    {
        /// <param name="contentType">Its media type.</param>
        /// <param name="content">Its bytes, to be read from their start.</param>
        public DerivedFileContent(string contentType, Stream content)
        {
            ArgumentNullException.ThrowIfNull(contentType);
            ArgumentNullException.ThrowIfNull(content);
            ContentType = contentType;
            Content = content;
        }

        /// <summary>Its media type, as the plugin that made it says.</summary>
        public string ContentType { get; }

        /// <summary>Its bytes.</summary>
        public Stream Content { get; }

        /// <summary>Disposes <see cref="Content"/>.</summary>
        public void Dispose()
        {
            Content.Dispose();
        }

        /// <inheritdoc cref="Dispose"/>
        public ValueTask DisposeAsync()
        {
            return Content.DisposeAsync();
        }
    }
}
