namespace ByteShelfCommon
{
    /// <summary>Sets a file's description, or removes it.</summary>
    public sealed class SetDescriptionRequest
    {
        /// <summary>The new description, at most <see cref="FileDescription.MaxLength"/> characters; null or blank removes it.</summary>
        public string? Description { get; init; }
    }
}
