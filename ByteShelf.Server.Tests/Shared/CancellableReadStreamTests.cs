using ByteShelf.Server.Shared.IO;

namespace ByteShelf.Server.Tests.Shared
{
    public class CancellableReadStreamTests
    {
        public static TheoryData<string> Reads => new TheoryData<string>(
            "Read(byte[])", "Read(Span)", "ReadByte", "ReadAsync(byte[])", "ReadAsync(Memory)", "Seek", "Position", "CopyTo", "CopyToAsync", "BeginRead");

        [Theory]
        [MemberData(nameof(Reads))]
        public async Task Read_OnceCancelled_IsRefusedHoweverItIsMade(string read)
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            await using CancellableReadStream stream = new CancellableReadStream(new MemoryStream([1, 2, 3]), cancellation.Token);
            await ReadAsync(stream, read);
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAsync(stream, read));
        }

        [Fact]
        public async Task Dispose_ClosesTheInnerStream()
        {
            MemoryStream inner = new MemoryStream([1]);
            MemoryStream innerAsync = new MemoryStream([1]);

            new CancellableReadStream(inner, CancellationToken.None).Dispose();
            await new CancellableReadStream(innerAsync, CancellationToken.None).DisposeAsync();

            Assert.False(inner.CanRead);
            Assert.False(innerAsync.CanRead);
        }

        private static async Task ReadAsync(Stream stream, string read)
        {
            byte[] buffer = new byte[1];
            switch (read)
            {
                case "Read(byte[])":
                    _ = stream.Read(buffer, 0, 1);
                    break;
                case "Read(Span)":
                    _ = stream.Read(buffer.AsSpan());
                    break;
                case "ReadByte":
                    _ = stream.ReadByte();
                    break;
                case "ReadAsync(byte[])":
#pragma warning disable CA1835 // The array overload is the one under test.
                    _ = await stream.ReadAsync(buffer, 0, 1, CancellationToken.None);
#pragma warning restore CA1835
                    break;
                case "ReadAsync(Memory)":
                    _ = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);
                    break;
                case "Seek":
                    stream.Seek(0, SeekOrigin.Begin);
                    break;
                case "Position":
                    stream.Position = 0;
                    break;
                case "CopyTo":
                    stream.CopyTo(Stream.Null);
                    break;
                case "CopyToAsync":
                    await stream.CopyToAsync(Stream.Null, CancellationToken.None);
                    break;
                case "BeginRead":
                    await Task.Factory.FromAsync(stream.BeginRead(buffer, 0, 1, null, null), stream.EndRead);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(read));
            }
        }
    }
}
