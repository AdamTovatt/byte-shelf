namespace ByteShelfCommon
{
    /// <summary>The body of every error the server answers with.</summary>
    public sealed class ErrorResponse
    {
        /// <summary>A sentence for a person.</summary>
        public required string Message { get; init; }

        /// <summary>One of <see cref="ErrorCodes"/>, for a program; null when the error has none.</summary>
        public string? Code { get; init; }
    }
}
