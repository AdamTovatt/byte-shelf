using System.Text;
using ByteShelf.Plugins;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>The streams a maker writes what it makes to.</summary>
    public sealed class MadeFilesTests : IDisposable
    {
        private static readonly long MaxBytes = DerivedFileLimits.MaxBytesOf(DerivedKind.Thumbnail);

        private readonly string _folder = Directory.CreateTempSubdirectory("byteshelf-made-").FullName;
        private readonly MadeFiles _made;

        public MadeFilesTests()
        {
            _made = new MadeFiles(_folder, [DerivedKind.Thumbnail]);
        }

        [Fact]
        public async Task AddAsync_IsWrittenAndReadInAnyOrder()
        {
            byte[] read = new byte[5];

            await AddAsync(async file =>
            {
                await file.WriteAsync("jello world"u8.ToArray());
                file.Position = 0;
                file.WriteByte((byte)'h');
                file.SetLength(5);
                file.Seek(0, SeekOrigin.Begin);
                await file.ReadExactlyAsync(read);
            });

            Assert.Equal(("hello", "hello"), (Encoding.UTF8.GetString(read), await File.ReadAllTextAsync(Path.Combine(_folder, "thumbnail"))));
            Assert.Equal(DerivedKind.Thumbnail, Assert.Single(_made.Made).Kind);
            _made.ThrowIfNotWritten();
        }

        [Fact]
        public async Task AddAsync_AsLargeAsItMayBe_IsMade()
        {
            await AddAsync(file =>
            {
                file.SetLength(MaxBytes);
                return Task.CompletedTask;
            });

            Assert.Single(_made.Made);
        }

        [Theory]
        [InlineData("length")]
        [InlineData("write")]
        [InlineData("write further on")]
        public async Task AddAsync_LargerThanItMayBe_IsRefusedAndNotMade(string how)
        {
            await Assert.ThrowsAsync<IOException>(() => AddAsync(file =>
            {
                switch (how)
                {
                    case "length":
                        file.SetLength(MaxBytes + 1);
                        break;
                    case "write":
                        file.Write(new byte[MaxBytes + 1]);
                        break;
                    default:
                        file.Position = MaxBytes;
                        file.WriteByte(1);
                        break;
                }

                return Task.CompletedTask;
            }));

            Assert.Empty(_made.Made);

            // The maker's doing: not a file that could not be written.
            _made.ThrowIfNotWritten();
        }

        [Fact]
        public async Task AddAsync_AWriteThatThrowsMidway_IsNotMadeAndItsFileClosed()
        {
            Stream? given = null;

            await Assert.ThrowsAsync<InvalidDataException>(() => AddAsync(async file =>
            {
                given = file;
                await file.WriteAsync("half"u8.ToArray());
                throw new InvalidDataException("The rest is damaged.");
            }));

            Assert.Empty(_made.Made);
            Assert.False(given!.CanWrite);
            _made.ThrowIfNotWritten();
        }

        public void Dispose()
        {
            Directory.Delete(_folder, recursive: true);
        }

        private Task AddAsync(Func<Stream, Task> write)
        {
            return _made.AddAsync(DerivedKind.Thumbnail, "image/webp", write);
        }
    }
}
