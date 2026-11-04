namespace ByteShelf.Server.Invalidation.Models
{
    /// <summary>
    /// A cache-invalidation notification pushed to connected clients: <paramref name="Resource"/>
    /// names one of the <see cref="InvalidationResources"/> constants; optional
    /// <paramref name="Metadata"/> narrows the scope (e.g. an entity id).
    /// </summary>
    public record InvalidationEvent(string Resource, string? Metadata = null);
}
