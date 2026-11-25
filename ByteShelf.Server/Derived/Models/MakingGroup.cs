namespace ByteShelf.Server.Derived.Models
{
    /// <summary>The kinds of derived files made in one run, from one read of the contents.</summary>
    public enum MakingGroup
    {
        /// <summary>Those made for every file that has them.</summary>
        Background,

        /// <summary><see cref="ByteShelfCommon.DerivedKind.Full"/>, made when asked for.</summary>
        Full,

        /// <summary><see cref="ByteShelfCommon.DerivedKind.Agent"/>, made when asked for.</summary>
        Agent,
    }
}
