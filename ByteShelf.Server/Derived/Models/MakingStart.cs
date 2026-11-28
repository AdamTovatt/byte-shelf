namespace ByteShelf.Server.Derived.Models
{
    /// <summary>
    /// What has a group of derived files made (<see cref="MakingGroup.Start"/>): someone asking for one that is missing,
    /// the background, or both. One made by neither cannot be written.
    /// </summary>
    public sealed class MakingStart
    {
        /// <summary>Made as someone asks for one that is missing, and waited for a while.</summary>
        public static readonly MakingStart WhenAsked = new MakingStart(job: null, madeWhenAsked: true);

        private MakingStart(string? job, bool madeWhenAsked)
        {
            Job = job;
            MadeWhenAsked = madeWhenAsked;
        }

        /// <summary>
        /// The job that makes it in the background, for every file that has it, of contents as they are stored and of those
        /// stored already; null for a group made only when asked for.
        /// </summary>
        public string? Job { get; }

        /// <summary>
        /// Whether a missing one is made as someone asks for it. One that is not is left to the background: making it takes
        /// too long to be waited for.
        /// </summary>
        public bool MadeWhenAsked { get; }

        /// <summary>Made in the background by <paramref name="job"/> alone.</summary>
        public static MakingStart InBackground(string job)
        {
            return new MakingStart(job, madeWhenAsked: false);
        }

        /// <summary>Made in the background by <paramref name="job"/>, and as someone asks for one that is missing.</summary>
        public static MakingStart WhenAskedAndInBackground(string job)
        {
            return new MakingStart(job, madeWhenAsked: true);
        }
    }
}
