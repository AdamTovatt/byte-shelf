using System.Text.Json.Serialization;
using ByteShelfCommon;

namespace ByteShelf.Cli.Config
{
    /// <summary>What <c>byteshelf login</c> keeps: the server and one way to prove who you are there.</summary>
    internal sealed class CliConfig
    {
        /// <summary>The server's address, ending in <c>/</c>.</summary>
        public required string Server { get; init; }

        public required CliLogin Login { get; init; }
    }

    /// <summary>A shelf key or a signed-in user's session, never both.</summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(KeyLogin), "key")]
    [JsonDerivedType(typeof(SessionLogin), "session")]
    internal abstract class CliLogin
    {
    }

    internal sealed class KeyLogin : CliLogin
    {
        /// <summary>The key's secret.</summary>
        public required string Key { get; init; }
    }

    internal sealed class SessionLogin : CliLogin
    {
        public required string Email { get; init; }

        /// <summary>The session's tokens, replaced at every renewal.</summary>
        public required SessionTokens Tokens { get; init; }
    }
}
