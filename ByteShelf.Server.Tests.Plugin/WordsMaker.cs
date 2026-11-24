using System.Text;
using ByteShelf.Plugins;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Plugin
{
    /// <summary>Makes a text into itself in capitals, whatever kind is asked for.</summary>
    public sealed class WordsMaker : IDerivedFileMaker
    {
        public async Task MakeAsync(Stream contents, IReadOnlyList<DerivedKind> kinds, IMadeFiles made)
        {
            using StreamReader reader = new StreamReader(contents, leaveOpen: true);
            byte[] words = Encoding.UTF8.GetBytes((await reader.ReadToEndAsync()).ToUpperInvariant());
            foreach (DerivedKind kind in kinds)
            {
                await made.AddAsync(kind, "text/plain", null, file => file.WriteAsync(words).AsTask());
            }
        }
    }
}
