namespace ByteShelfCommon
{
    /// <summary>One change in a <see cref="SetDescriptionsRequest"/>: the file at <see cref="Path"/> gets <see cref="Description"/>.</summary>
    public sealed class DescriptionChange
    {
        /// <summary>The file's path.</summary>
        public required string Path { get; init; }

        /// <summary>Its new description, as <see cref="SetDescriptionRequest.Description"/>; null or blank removes it.</summary>
        public string? Description { get; init; }
    }
}
