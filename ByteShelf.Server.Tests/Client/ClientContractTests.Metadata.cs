using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>
    /// File metadata in both clients: what holds for both. Only the server reads contents, with libraries
    /// the published client does not carry, so the in-memory client's files have none
    /// (<see cref="FileMetadata.None"/>) and what is read is tested against the server only, in
    /// <see cref="HttpClientContractTests"/>.
    /// </summary>
    public abstract partial class ClientContractTests
    {
        [Fact]
        public async Task GetFileMetadataAsync_TextFile_HasNoneRead()
        {
            await UploadTextAsync("/notes.txt", "plain words", contentType: "text/plain");

            FileMetadata metadata = await Shelf.GetFileMetadataAsync("/notes.txt");

            Assert.Null(metadata.ReadAt);
        }

        [Fact]
        public async Task GetFileMetadataAsync_MissingFile_ThrowsNotFound()
        {
            ByteShelfException exception = await RefusalOf(Shelf.GetFileMetadataAsync("/missing.pdf"));

            Assert.Equal(ErrorCodes.NotFound, exception.Code);
        }
    }
}
