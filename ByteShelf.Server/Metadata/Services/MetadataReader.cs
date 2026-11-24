using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ByteShelf.Server.Metadata.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.Util;
using UglyToad.PdfPig;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// Reads what a file's contents say about it, with managed libraries and nothing outside the process:
    /// MetadataExtractor for photos (EXIF, HEIC included) and QuickTime/MP4 videos, PdfPig for PDFs. It
    /// reads the headers only and never decodes an image's pixels or a page's contents.
    /// </summary>
    public static partial class MetadataReader
    {
        /// <summary>Raised when what it reads changes: contents an earlier version read are read again.</summary>
        public const int Version = 1;

        /// <summary>
        /// The most code points a title or camera keeps; what a file carries past that is cut. The
        /// database holds the same limit (v0012), as <c>length()</c> counts.
        /// </summary>
        public const int MaxTextLength = 1000;

        /// <summary>How far into the contents a PDF's <c>%PDF-</c> header may start, as readers allow.</summary>
        private const int PdfHeaderWindow = 1024;

        private static readonly DateTime QuickTimeEpoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// What <paramref name="contents"/>, a seekable stream at its start, say, told by their own bytes
        /// rather than the type the file was stored with; <see cref="ContentMetadata.None"/> for contents
        /// of a kind it does not know.
        /// </summary>
        /// <exception cref="Exception">The contents are damaged: the libraries throw what their parsers meet.</exception>
        public static ContentMetadata Read(Stream contents)
        {
            if (StartsAsPdf(contents))
            {
                return ReadPdf(contents);
            }

            FileType format = FileTypeDetector.DetectFileType(contents);
            contents.Position = 0;
            if (format == FileType.Unknown)
            {
                return ContentMetadata.None;
            }

            IReadOnlyList<MetadataExtractor.Directory> directories = ImageMetadataReader.ReadMetadata(contents);
            (double Latitude, double Longitude)? position = PositionOf(directories);
            return new ContentMetadata
            {
                TakenAt = TakenAtOf(directories),
                Latitude = position?.Latitude,
                Longitude = position?.Longitude,
                DurationSeconds = DurationOf(directories),
                Camera = CameraOf(directories),
            };
        }

        /// <remarks>
        /// A photo whose EXIF holds the text <c>%PDF-</c> in its first kilobyte would be read as a PDF and
        /// end up damaged. No real file is like that, and a check for it would cost more than it saves.
        /// </remarks>
        private static bool StartsAsPdf(Stream contents)
        {
            byte[] start = new byte[PdfHeaderWindow];
            int read = contents.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
            contents.Position = 0;
            return start.AsSpan(0, read).IndexOf("%PDF-"u8) >= 0;
        }

        private static ContentMetadata ReadPdf(Stream contents)
        {
            using PdfDocument document = PdfDocument.Open(contents, new ParsingOptions { UseLenientParsing = true });
            return new ContentMetadata { PageCount = document.NumberOfPages, Title = Text(document.Information.Title) };
        }

        /// <summary>
        /// When a photo was taken by its EXIF (the original time, else the digitized one, each with its own
        /// offset when the camera wrote one), else when a video was by its QuickTime metadata.
        /// </summary>
        private static DateTimeOffset? TakenAtOf(IReadOnlyList<MetadataExtractor.Directory> directories)
        {
            foreach (ExifSubIfdDirectory exif in directories.OfType<ExifSubIfdDirectory>())
            {
                if (ExifTime(exif, ExifDirectoryBase.TagDateTimeOriginal, ExifDirectoryBase.TagTimeZoneOriginal) is DateTimeOffset original)
                {
                    return original;
                }

                if (ExifTime(exif, ExifDirectoryBase.TagDateTimeDigitized, ExifDirectoryBase.TagTimeZoneDigitized) is DateTimeOffset digitized)
                {
                    return digitized;
                }
            }

            // Apple's creation date carries its offset, which the library applies, handing it back in the
            // server's local time; the movie header's is UTC by the format, and zero (the QuickTime or
            // Unix epoch) where the camera set none.
            foreach (QuickTimeMetadataHeaderDirectory quickTime in directories.OfType<QuickTimeMetadataHeaderDirectory>())
            {
                if (quickTime.TryGetDateTime(QuickTimeMetadataHeaderDirectory.TagCreationDate, out DateTime created) && IsSet(created))
                {
                    return AsUtc(created);
                }
            }

            foreach (QuickTimeMovieHeaderDirectory movie in directories.OfType<QuickTimeMovieHeaderDirectory>())
            {
                if (movie.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out DateTime created) && IsSet(created))
                {
                    return AsUtc(created);
                }
            }

            return null;
        }

        private static DateTimeOffset? ExifTime(ExifSubIfdDirectory exif, int timeTag, int offsetTag)
        {
            if (!exif.TryGetDateTime(timeTag, out DateTime time))
            {
                return null;
            }

            // EXIF 2.31's offset, such as +02:00; a camera without one gives its clock's time, kept as UTC.
            TimeSpan offset = TimeSpan.Zero;
            Match match = ExifOffset().Match(exif.GetString(offsetTag)?.Trim() ?? string.Empty);
            if (match.Success)
            {
                offset = new TimeSpan(int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture), 0);
                offset = match.Groups["sign"].Value == "-" ? -offset : offset;
            }

            return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Unspecified), offset);
        }

        private static bool IsSet(DateTime time)
        {
            return time != QuickTimeEpoch && time != DateTime.UnixEpoch;
        }

        /// <summary>A time the library gives as local converted to UTC; one of no kind, or UTC already, taken as UTC.</summary>
        private static DateTimeOffset AsUtc(DateTime time)
        {
            DateTime utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);
            return new DateTimeOffset(utc);
        }

        /// <summary>
        /// Where it was taken: EXIF's GPS position, else QuickTime's ISO 6709 location. A position of
        /// exactly 0, 0 is what cameras write without a fix, so it counts as none.
        /// </summary>
        private static (double Latitude, double Longitude)? PositionOf(IReadOnlyList<MetadataExtractor.Directory> directories)
        {
            foreach (GpsDirectory gps in directories.OfType<GpsDirectory>())
            {
                if (gps.TryGetGeoLocation(out GeoLocation location) && Position(location.Latitude, location.Longitude) is { } position)
                {
                    return position;
                }
            }

            foreach (QuickTimeMetadataHeaderDirectory quickTime in directories.OfType<QuickTimeMetadataHeaderDirectory>())
            {
                Match match = Iso6709().Match(quickTime.GetString(QuickTimeMetadataHeaderDirectory.TagGpsLocation) ?? string.Empty);
                if (match.Success
                    && double.TryParse(match.Groups["latitude"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude)
                    && double.TryParse(match.Groups["longitude"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude)
                    && Position(latitude, longitude) is { } position)
                {
                    return position;
                }
            }

            return null;
        }

        private static (double Latitude, double Longitude)? Position(double latitude, double longitude)
        {
            bool onEarth = latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
            return onEarth && (latitude != 0 || longitude != 0) ? (latitude, longitude) : null;
        }

        private static double? DurationOf(IReadOnlyList<MetadataExtractor.Directory> directories)
        {
            foreach (QuickTimeMovieHeaderDirectory movie in directories.OfType<QuickTimeMovieHeaderDirectory>())
            {
                if (movie.GetObject(QuickTimeMovieHeaderDirectory.TagDuration) is TimeSpan duration && duration > TimeSpan.Zero)
                {
                    return duration.TotalSeconds;
                }
            }

            return null;
        }

        /// <summary>The camera as its make and model, from EXIF or QuickTime; the model alone when it already names the make.</summary>
        private static string? CameraOf(IReadOnlyList<MetadataExtractor.Directory> directories)
        {
            foreach (ExifIfd0Directory exif in directories.OfType<ExifIfd0Directory>())
            {
                if (Camera(exif.GetString(ExifDirectoryBase.TagMake), exif.GetString(ExifDirectoryBase.TagModel)) is string camera)
                {
                    return camera;
                }
            }

            foreach (QuickTimeMetadataHeaderDirectory quickTime in directories.OfType<QuickTimeMetadataHeaderDirectory>())
            {
                string? camera = Camera(quickTime.GetString(QuickTimeMetadataHeaderDirectory.TagMake), quickTime.GetString(QuickTimeMetadataHeaderDirectory.TagModel))
                    ?? Camera(quickTime.GetString(QuickTimeMetadataHeaderDirectory.TagAndroidManufacturer), quickTime.GetString(QuickTimeMetadataHeaderDirectory.TagAndroidModel));
                if (camera != null)
                {
                    return camera;
                }
            }

            return null;
        }

        private static string? Camera(string? make, string? model)
        {
            string? cleanMake = Text(make);
            string? cleanModel = Text(model);
            if (cleanMake == null || cleanModel == null)
            {
                return cleanModel ?? cleanMake;
            }

            return cleanModel.StartsWith(cleanMake, StringComparison.OrdinalIgnoreCase)
                ? cleanModel
                : Text($"{cleanMake} {cleanModel}");
        }

        /// <summary>Text a file carries, without control characters and surrounding space, cut to <see cref="MaxTextLength"/> code points; null for blank.</summary>
        private static string? Text(string? text)
        {
            if (text == null)
            {
                return null;
            }

            StringBuilder kept = new StringBuilder(text.Length);
            foreach (char character in text)
            {
                kept.Append(char.IsControl(character) ? ' ' : character);
            }

            string trimmed = kept.ToString().Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            string cut = CutToMaxLength(trimmed);
            return cut.Length == 0 ? null : cut;
        }

        /// <summary>
        /// The longest start of <paramref name="text"/> of at most <see cref="MaxTextLength"/> code points,
        /// as the database counts, that ends between text elements, so a cut never splits a character
        /// (an emoji with its modifiers, a letter with its accents) in two.
        /// </summary>
        private static string CutToMaxLength(string text)
        {
            int codePoints = 0;
            int end = 0;
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                codePoints += element.EnumerateRunes().Count();
                if (codePoints > MaxTextLength)
                {
                    return text[..end].TrimEnd();
                }

                end += element.Length;
            }

            return text;
        }

        [GeneratedRegex(@"^(?<latitude>[+-]\d+(?:\.\d+)?)(?<longitude>[+-]\d+(?:\.\d+)?)")]
        private static partial Regex Iso6709();

        [GeneratedRegex(@"^(?<sign>[+-])(?<hours>[01]\d|2[0-3]):(?<minutes>[0-5]\d)$")]
        private static partial Regex ExifOffset();
    }
}
