using ByteShelfCommon;

namespace ByteShelf.Cli.Commands
{
    /// <summary>What <c>byteshelf version</c> prints: the CLI's release and, once the server has answered, the server's.</summary>
    internal sealed class VersionReport
    {
        /// <summary>This CLI's release.</summary>
        public required string Cli { get; init; }

        /// <summary>The server's release and deploy id; null when no connection is set up or the server could not be asked.</summary>
        public ServerVersion? Server { get; init; }
    }
}
