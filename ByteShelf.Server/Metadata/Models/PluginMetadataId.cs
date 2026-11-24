using System.Security.Cryptography;
using System.Text;

namespace ByteShelf.Server.Metadata.Models
{
    /// <summary>
    /// The id of a row a plugin has one of: the change log names an entity by a Guid, and one made from the
    /// plugin and what the row is about means a primary and its replicas never hold two rows about the same.
    /// </summary>
    internal static class PluginMetadataId
    {
        /// <param name="pluginId">The plugin, whose id holds no colon.</param>
        /// <param name="about">What the row is about, as text.</param>
        public static Guid For(string pluginId, string about)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{pluginId}:{about}"));
            return new Guid(hash.AsSpan(0, 16));
        }
    }
}
