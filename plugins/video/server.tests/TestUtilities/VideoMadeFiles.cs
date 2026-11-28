using ByteShelf.Plugins.Testing;
using ByteShelfCommon;

namespace ByteShelf.Plugins.Video.Tests.TestUtilities
{
    /// <summary>What the video maker makes, taken as the server takes it (<see cref="MadeFiles"/>).</summary>
    internal static class VideoMadeFiles
    {
        extension(MadeFiles)
        {
            public static async Task<MadeFiles> MakeAsync(byte[] contents, params IReadOnlyList<DerivedKind> kinds)
            {
                using MemoryStream stream = new MemoryStream(contents, writable: false);
                return await MadeFiles.MakeAsync(stream, kinds);
            }

            public static Task<MadeFiles> MakeAsync(Stream contents, params IReadOnlyList<DerivedKind> kinds)
            {
                return MadeFiles.MakeAsync(new VideoMaker(), contents, kinds);
            }
        }
    }
}
