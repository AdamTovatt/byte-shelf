using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ByteShelf.Plugins.Video
{
    /// <summary>
    /// The contents over HTTP on the loopback, at an address no one else can guess, for ffmpeg and ffprobe to read by
    /// ranges, so that they seek in them and read only the parts they need, as a browser does. A read is one at a time
    /// (<see cref="BlockCache"/>), whatever asks for it: the server's reads of the contents are.
    /// </summary>
    internal sealed class RangeServer : IAsyncDisposable
    {
        /// <summary>More than ffmpeg sends of a request's head.</summary>
        private const int MaxHeadBytes = 16 * 1024;

        private const int ChunkBytes = 64 * 1024;

        private readonly TcpListener _listener;
        private readonly string _path;
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private readonly List<Task> _serving = [];
        private readonly Task _accepting;

        private RangeServer(Stream contents)
        {
            Contents = new BlockCache(contents);
            _path = "/" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{_path}";
            _accepting = AcceptAsync();
        }

        /// <summary>Where the contents are.</summary>
        public string Url { get; }

        /// <summary>The contents, which are read here too, as the server reads them.</summary>
        public BlockCache Contents { get; }

        /// <summary>Serves <paramref name="contents"/>, a seekable stream, until it is disposed of.</summary>
        public static RangeServer Start(Stream contents)
        {
            return new RangeServer(contents);
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Dispose();
            await _accepting;
            Task[] serving;
            lock (_serving)
            {
                serving = [.. _serving];
            }

            await Task.WhenAll(serving);
            _stopping.Dispose();
            Contents.Dispose();
        }

        private async Task AcceptAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                }
                catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    return;
                }

                lock (_serving)
                {
                    _serving.RemoveAll(task => task.IsCompleted);
                    _serving.Add(ServeAsync(client));
                }
            }
        }

        /// <summary>One request, after which the connection is closed, as ffmpeg asks.</summary>
        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.SendBufferSize = ChunkBytes;
                    NetworkStream network = client.GetStream();
                    if (await ReadHeadAsync(network, _stopping.Token) is not { } head)
                    {
                        return;
                    }

                    await AnswerAsync(network, head, _stopping.Token);
                }
                catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                {
                    // It hung up, as ffmpeg does when it seeks, or the server stopped.
                }
            }
        }

        private async Task AnswerAsync(NetworkStream network, string head, CancellationToken cancellationToken)
        {
            string[] lines = head.Split("\r\n");
            string[] request = lines[0].Split(' ');
            if (request.Length != 3 || request[1] != _path)
            {
                await WriteHeadAsync(network, "404 Not Found", 0, null, cancellationToken);
                return;
            }

            if (request[0] != "GET")
            {
                await WriteHeadAsync(network, "405 Method Not Allowed", 0, null, cancellationToken);
                return;
            }

            long length = Contents.Length;
            string? range = lines.Skip(1)
                .Select(line => line.Split(':', 2))
                .Where(header => header.Length == 2 && header[0].Trim().Equals("Range", StringComparison.OrdinalIgnoreCase))
                .Select(header => header[1].Trim())
                .FirstOrDefault();
            if (range == null)
            {
                await WriteHeadAsync(network, "200 OK", length, null, cancellationToken);
                await WriteBodyAsync(network, 0, length, cancellationToken);
                return;
            }

            if (RangeOf(range, length) is not ({ } start, { } end))
            {
                await WriteHeadAsync(network, "416 Range Not Satisfiable", 0, $"bytes */{length}", cancellationToken);
                return;
            }

            await WriteHeadAsync(network, "206 Partial Content", end - start + 1, $"bytes {start}-{end}/{length}", cancellationToken);
            await WriteBodyAsync(network, start, end + 1, cancellationToken);
        }

        /// <summary>The first and last byte of a range of one part, <c>bytes=a-</c> or <c>bytes=a-b</c>; null when it asks for none of the contents.</summary>
        internal static (long Start, long End)? RangeOf(string range, long length)
        {
            if (!range.StartsWith("bytes=", StringComparison.Ordinal) || range.Contains(','))
            {
                return null;
            }

            string[] bounds = range["bytes=".Length..].Split('-');
            if (bounds.Length != 2
                || !long.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out long start)
                || start >= length)
            {
                return null;
            }

            if (bounds[1].Length == 0)
            {
                return (start, length - 1);
            }

            return long.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out long end) && end >= start
                ? (start, Math.Min(end, length - 1))
                : null;
        }

        private static async Task<string?> ReadHeadAsync(NetworkStream network, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[MaxHeadBytes];
            int read = 0;
            while (read < buffer.Length)
            {
                int last = await network.ReadAsync(buffer.AsMemory(read), cancellationToken);
                if (last == 0)
                {
                    return null;
                }

                read += last;
                int end = buffer.AsSpan(0, read).IndexOf("\r\n\r\n"u8);
                if (end >= 0)
                {
                    return Encoding.Latin1.GetString(buffer, 0, end);
                }
            }

            return null;
        }

        private static async Task WriteHeadAsync(NetworkStream network, string status, long length, string? contentRange, CancellationToken cancellationToken)
        {
            StringBuilder head = new StringBuilder()
                .Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {status}\r\n")
                .Append("Accept-Ranges: bytes\r\n")
                .Append("Connection: close\r\n")
                .Append("Content-Type: application/octet-stream\r\n")
                .Append(CultureInfo.InvariantCulture, $"Content-Length: {length}\r\n");
            if (contentRange != null)
            {
                head.Append(CultureInfo.InvariantCulture, $"Content-Range: {contentRange}\r\n");
            }

            await network.WriteAsync(Encoding.Latin1.GetBytes(head.Append("\r\n").ToString()), cancellationToken);
        }

        private async Task WriteBodyAsync(NetworkStream network, long from, long to, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[ChunkBytes];
            for (long position = from; position < to;)
            {
                int read = await Contents.ReadAsync(position, buffer.AsMemory(0, (int)Math.Min(buffer.Length, to - position)), cancellationToken);
                if (read == 0)
                {
                    // The contents are shorter than they said, which ends the answer short.
                    return;
                }

                await network.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                position += read;
            }
        }
    }
}
