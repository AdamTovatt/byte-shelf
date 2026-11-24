using System.Text.Json.Serialization;

namespace ByteShelf.Server.Derived.Models
{
    /// <summary>How making one kind of derived file of some contents ended: one of its cases, each with what it alone knows.</summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "was")]
    [JsonDerivedType(typeof(Made), "made")]
    [JsonDerivedType(typeof(Failed), "failed")]
    [JsonDerivedType(typeof(NoRoom), "noRoom")]
    public abstract record DerivedOutcome
    {
        private DerivedOutcome()
        {
        }

        /// <summary>It was made and kept.</summary>
        /// <param name="MediaType">Its media type.</param>
        public sealed record Made(string MediaType) : DerivedOutcome;

        /// <summary>Making it failed because of the contents, which that version does not try again.</summary>
        public sealed record Failed : DerivedOutcome;

        /// <summary>
        /// It was made but not kept, as a shelf that holds the contents had no room for it. It is made again once they all
        /// have room for as many bytes.
        /// </summary>
        /// <param name="Bytes">How many bytes it took.</param>
        public sealed record NoRoom(long Bytes) : DerivedOutcome;
    }
}
