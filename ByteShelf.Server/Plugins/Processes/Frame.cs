using System.Buffers.Binary;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// What the server and a plugin's process say to each other, over the process's standard input and output: a
    /// byte that says what (<see cref="FrameType"/>), four with the payload's length, and the payload.
    /// </summary>
    internal sealed record Frame(FrameType Type, byte[] Payload)
    {
        /// <summary>The longest payload.</summary>
        public const int MaxPayloadBytes = 1024 * 1024;

        private const int HeaderBytes = 5;

        /// <summary>The next one; null when the other side is gone.</summary>
        /// <exception cref="InvalidDataException">What was sent is none.</exception>
        public static async ValueTask<Frame?> ReadAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] header = new byte[HeaderBytes];
            if (await stream.ReadAtLeastAsync(header, HeaderBytes, throwOnEndOfStream: false, cancellationToken) < HeaderBytes)
            {
                return null;
            }

            byte[] payload = new byte[LengthIn(header)];
            return await stream.ReadAtLeastAsync(payload, payload.Length, throwOnEndOfStream: false, cancellationToken) < payload.Length
                ? null
                : new Frame((FrameType)header[0], payload);
        }

        /// <summary>As <see cref="ReadAsync"/>, on the caller's thread.</summary>
        public static Frame? Read(Stream stream)
        {
            byte[] header = new byte[HeaderBytes];
            if (stream.ReadAtLeast(header, HeaderBytes, throwOnEndOfStream: false) < HeaderBytes)
            {
                return null;
            }

            byte[] payload = new byte[LengthIn(header)];
            return stream.ReadAtLeast(payload, payload.Length, throwOnEndOfStream: false) < payload.Length ? null : new Frame((FrameType)header[0], payload);
        }

        public static async ValueTask WriteAsync(Stream stream, FrameType type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(Header(type, payload.Length), cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        /// <summary>As <see cref="WriteAsync"/>, on the caller's thread.</summary>
        public static void Write(Stream stream, FrameType type, ReadOnlySpan<byte> payload)
        {
            stream.Write(Header(type, payload.Length));
            stream.Write(payload);
            stream.Flush();
        }

        private static byte[] Header(FrameType type, int length)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, MaxPayloadBytes);
            byte[] header = new byte[HeaderBytes];
            header[0] = (byte)type;
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), length);
            return header;
        }

        private static int LengthIn(byte[] header)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1));
            return length is >= 0 and <= MaxPayloadBytes ? length : throw new InvalidDataException("What was sent is no frame.");
        }
    }
}
