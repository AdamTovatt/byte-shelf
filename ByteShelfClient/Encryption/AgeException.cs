namespace ByteShelfClient.Encryption
{
    /// <summary>Why an age file could not be decrypted.</summary>
    public enum AgeFailure
    {
        /// <summary>The header is not a valid age v1 header, or its MAC does not match.</summary>
        Header,

        /// <summary>No stanza could be opened: for a password, it is wrong or the file is not encrypted with one.</summary>
        NoMatch,

        /// <summary>The contents after the header are damaged or cut short.</summary>
        Payload,

        /// <summary>Its password's work factor asks for more memory than this process could have.</summary>
        OutOfMemory,
    }

    /// <summary>An age file that could not be decrypted, and why.</summary>
    public sealed class AgeException : Exception
    {
        /// <summary>A failure of the kind <paramref name="failure"/>, said in <paramref name="message"/>.</summary>
        public AgeException(AgeFailure failure, string message)
            : base(message)
        {
            Failure = failure;
        }

        /// <summary>What went wrong.</summary>
        public AgeFailure Failure { get; }
    }
}
