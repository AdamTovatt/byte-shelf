namespace ByteShelf.Server.Derived.Models
{
    /// <summary>What becomes of a missing derived file of a group made in the background alone (<see cref="MakingStart.MadeWhenAsked"/>).</summary>
    internal enum BackgroundMaking
    {
        /// <summary>The background has yet to make it, or is making it.</summary>
        Later,

        /// <summary>The background makes none of the contents, as once only the trash holds them.</summary>
        None,

        /// <summary>The plugin's runs in the group's lane could not run when it last had one to do, so none is made until one can.</summary>
        CannotRun,
    }
}
