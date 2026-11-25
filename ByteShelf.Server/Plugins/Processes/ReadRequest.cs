using System.Buffers.Binary;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>Some of the contents, as a process asks for it (<see cref="FrameType.Read"/>).</summary>
    internal readonly record struct ReadRequest(long Offset, int Count)
    {
        private const int Bytes = sizeof(long) + sizeof(int);

        public byte[] ToPayload()
        {
            byte[] payload = new byte[Bytes];
            BinaryPrimitives.WriteInt64LittleEndian(payload, Offset);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(sizeof(long)), Count);
            return payload;
        }

        /// <exception cref="InvalidDataException">It asks for nothing that can be answered.</exception>
        public static ReadRequest From(byte[] payload)
        {
            if (payload.Length == Bytes)
            {
                ReadRequest request = new ReadRequest(BinaryPrimitives.ReadInt64LittleEndian(payload), BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(sizeof(long))));
                if (request is { Offset: >= 0, Count: >= 0 and <= Frame.MaxPayloadBytes })
                {
                    return request;
                }
            }

            throw new InvalidDataException("What was sent asks for no bytes.");
        }
    }
}
