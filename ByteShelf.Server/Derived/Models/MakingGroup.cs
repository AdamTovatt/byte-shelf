namespace ByteShelf.Server.Derived.Models
{
    /// <summary>The kinds of derived files made in one run, from one read of the contents, made as <see cref="DerivedKinds.RulesOf"/> says.</summary>
    public enum MakingGroup
    {
        /// <summary>Those made for every file that has them.</summary>
        Background,

        /// <summary><see cref="ByteShelfCommon.DerivedKind.Full"/>, made when asked for.</summary>
        Full,

        /// <summary><see cref="ByteShelfCommon.DerivedKind.Agent"/>, made when asked for.</summary>
        Agent,

        /// <summary><see cref="ByteShelfCommon.DerivedKind.Stream"/>, made for every file that has one, in runs that may take hours.</summary>
        Stream,
    }
}
