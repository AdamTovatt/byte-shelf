using System.Buffers.Binary;
using System.Text;

namespace ByteShelf.Plugins.Video
{
    /// <summary>
    /// What an MP4's index (<c>moov</c>) says of seeking in it, read from its boxes: whether it comes before the media
    /// (<c>mdat</c>), so a browser has it at once, and the most time between two keyframes of its first video track and
    /// from the last to the end, by the track's sample tables, over the whole video. Only the boxes leading to those are
    /// read.
    /// </summary>
    internal sealed record Mp4Index(bool IndexAtFront, double? MaxKeyframeGapSeconds)
    {
        /// <summary>A larger index is taken for none: one of a film of hours is some megabytes.</summary>
        internal const int MaxIndexBytes = 64 * 1024 * 1024;

        private const int HeaderBytes = 8;

        /// <summary>A header whose size is said in 64 bits.</summary>
        private const int LargeHeaderBytes = 16;

        /// <summary>What the boxes of <paramref name="contents"/> say; null when they are no MP4's.</summary>
        public static async Task<Mp4Index?> ReadAsync(BlockCache contents)
        {
            bool first = true;
            for (long position = 0; position + HeaderBytes <= contents.Length;)
            {
                if (await BoxAtAsync(contents, position, contents.Length) is not { } box)
                {
                    return null;
                }

                if (first && box.Type != "ftyp")
                {
                    return null;
                }

                first = false;
                switch (box.Type)
                {
                    case "mdat":
                        return new Mp4Index(IndexAtFront: false, MaxKeyframeGapSeconds: null);
                    case "moov":
                        if (box.BodyLength > MaxIndexBytes)
                        {
                            return new Mp4Index(IndexAtFront: true, MaxKeyframeGapSeconds: null);
                        }

                        byte[] index = await contents.ReadExactlyAsync(box.BodyStart, (int)box.BodyLength);
                        return new Mp4Index(IndexAtFront: true, MaxKeyframeGapSeconds: KeyframeGapOf(index));
                }

                position = box.End;
            }

            return null;
        }

        /// <summary>The most seconds between keyframes of the first video track in <paramref name="moov"/>'s body; null when it has none it can tell, as a fragmented MP4's, whose samples are elsewhere.</summary>
        internal static double? KeyframeGapOf(ReadOnlySpan<byte> moov)
        {
            foreach ((string type, int start, int length) in Children(moov))
            {
                if (type == "trak" && VideoTrackGap(moov.Slice(start, length)) is { } gap)
                {
                    return gap;
                }
            }

            return null;
        }

        private static double? VideoTrackGap(ReadOnlySpan<byte> trak)
        {
            if (!TryChild(trak, "mdia", out ReadOnlySpan<byte> mdia)
                || !TryChild(mdia, "hdlr", out ReadOnlySpan<byte> hdlr)
                || hdlr.Length < 12
                || Encoding.Latin1.GetString(hdlr.Slice(8, 4)) != "vide"
                || !TryChild(mdia, "mdhd", out ReadOnlySpan<byte> mdhd)
                || Timescale(mdhd) is not { } timescale
                || !TryChild(mdia, "minf", out ReadOnlySpan<byte> minf)
                || !TryChild(minf, "stbl", out ReadOnlySpan<byte> stbl)
                || !TryChild(stbl, "stts", out ReadOnlySpan<byte> stts)
                || Table(stts, fields: 2) is not { } times)
            {
                return null;
            }

            uint[]? sync = null;
            if (TryChild(stbl, "stss", out ReadOnlySpan<byte> stss) && (sync = Table(stss, fields: 1)) == null)
            {
                return null;
            }

            (uint Count, uint Delta)[] runs = [.. Enumerable.Range(0, times.Length / 2).Select(entry => (times[entry * 2], times[(entry * 2) + 1]))];
            return GapOf(runs, sync, timescale);
        }

        /// <summary>
        /// The most time between keyframes, from the start to the first and from the last to the end. <paramref name="runs"/>
        /// are <c>stts</c>'s: so many samples, each so long; <paramref name="sync"/> the keyframes' numbers, from 1, or null
        /// when every sample is one. Null when there are no samples, or no keyframe among them. Times are counted in
        /// doubles, which a table of any numbers neither wraps nor overflows.
        /// </summary>
        internal static double? GapOf(IReadOnlyList<(uint Count, uint Delta)> runs, uint[]? sync, uint timescale)
        {
            double samples = runs.Sum(run => (double)run.Count);
            if (samples == 0)
            {
                return null;
            }

            if (sync == null)
            {
                return (double)runs.Where(run => run.Count > 0).Max(run => run.Delta) / timescale;
            }

            double gap = 0;
            double last = 0;
            bool any = false;
            int at = 0;
            double runFirst = 1;
            double runTime = 0;
            foreach (uint number in sync.Where(number => number >= 1 && number <= samples).Distinct().Order())
            {
                while (number >= runFirst + runs[at].Count)
                {
                    runFirst += runs[at].Count;
                    runTime += (double)runs[at].Count * runs[at].Delta;
                    at++;
                }

                double time = runTime + ((number - runFirst) * runs[at].Delta);
                gap = Math.Max(gap, time - last);
                last = time;
                any = true;
            }

            if (!any)
            {
                return null;
            }

            double end = runs.Sum(run => (double)run.Count * run.Delta);
            return Math.Max(gap, end - last) / timescale;
        }

        private static uint? Timescale(ReadOnlySpan<byte> mdhd)
        {
            int at = mdhd.Length > 0 && mdhd[0] == 1 ? 20 : 12;
            if (mdhd.Length < at + 4)
            {
                return null;
            }

            uint timescale = BinaryPrimitives.ReadUInt32BigEndian(mdhd[at..]);
            return timescale == 0 ? null : timescale;
        }

        /// <summary>
        /// A full box's table of entries of <paramref name="fields"/> 32-bit numbers each, as <c>stts</c>'s pairs or
        /// <c>stss</c>'s numbers, one after another; null when it is broken.
        /// </summary>
        private static uint[]? Table(ReadOnlySpan<byte> box, int fields)
        {
            if (box.Length < 8)
            {
                return null;
            }

            uint count = BinaryPrimitives.ReadUInt32BigEndian(box[4..]);
            if (count > (box.Length - 8) / (4 * fields))
            {
                return null;
            }

            uint[] numbers = new uint[count * fields];
            for (int number = 0; number < numbers.Length; number++)
            {
                numbers[number] = BinaryPrimitives.ReadUInt32BigEndian(box[(8 + (number * 4))..]);
            }

            return numbers;
        }

        /// <summary>The body of the first box of <paramref name="type"/> in <paramref name="body"/>.</summary>
        private static bool TryChild(ReadOnlySpan<byte> body, string type, out ReadOnlySpan<byte> child)
        {
            foreach ((string childType, int start, int length) in Children(body))
            {
                if (childType == type)
                {
                    child = body.Slice(start, length);
                    return true;
                }
            }

            child = default;
            return false;
        }

        /// <summary>The boxes in <paramref name="body"/>, each by its type and where its body is in it, until one is broken.</summary>
        private static List<(string Type, int Start, int Length)> Children(ReadOnlySpan<byte> body)
        {
            List<(string Type, int Start, int Length)> children = [];
            for (int position = 0; HeaderOf(body[position..], body.Length - position) is { } box;)
            {
                children.Add((box.Type, position + box.HeaderLength, (int)(box.Size - box.HeaderLength)));
                position += (int)box.Size;
            }

            return children;
        }

        private static async Task<(string Type, long BodyStart, long BodyLength, long End)?> BoxAtAsync(BlockCache contents, long position, long end)
        {
            byte[] header = await contents.ReadExactlyAsync(position, (int)Math.Min(LargeHeaderBytes, end - position));
            if (HeaderOf(header, end - position) is not { } box)
            {
                return null;
            }

            return (box.Type, position + box.HeaderLength, box.Size - box.HeaderLength, position + box.Size);
        }

        /// <summary>
        /// The box whose header <paramref name="header"/> starts with, with <paramref name="room"/> bytes from its start to
        /// the end of what holds it: its type, its header's length and its size, header and all. A size of 1 is said in 64
        /// bits after the type, and one of 0 is all the room. Null when its header is cut short or its size does not fit.
        /// </summary>
        internal static (string Type, int HeaderLength, long Size)? HeaderOf(ReadOnlySpan<byte> header, long room)
        {
            if (header.Length < HeaderBytes)
            {
                return null;
            }

            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string type = Encoding.Latin1.GetString(header.Slice(4, 4));
            int headerLength = HeaderBytes;
            if (size == 1)
            {
                if (header.Length < LargeHeaderBytes)
                {
                    return null;
                }

                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[HeaderBytes..]);
                headerLength = LargeHeaderBytes;
            }
            else if (size == 0)
            {
                size = room;
            }

            return size < headerLength || size > room ? null : (type, headerLength, size);
        }
    }
}
