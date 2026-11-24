using ByteShelfCommon;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Records of files no server has.</summary>
    internal static class TestFiles
    {
        /// <summary>The record of a file of 1 byte at <paramref name="path"/>.</summary>
        public static ShelfFile Record(string path, string contentType, IReadOnlyDictionary<DerivedKind, string>? derived = null)
        {
            return new ShelfFile
            {
                Id = Guid.NewGuid(),
                ShelfId = Guid.NewGuid(),
                DirectoryId = Guid.NewGuid(),
                Path = path,
                Name = ShelfPath.GetName(path),
                ContentType = contentType,
                SizeBytes = 1,
                Sha256 = "a",
                CreatedAt = TestInstants.Now,
                UpdatedAt = TestInstants.Now,
                Encrypted = false,
                Derived = derived,
            };
        }
    }
}
