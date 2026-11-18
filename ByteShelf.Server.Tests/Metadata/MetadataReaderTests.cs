using System.Text;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Metadata
{
    public class MetadataReaderTests
    {
        [Fact]
        public void Read_PhotoWithEverything_ReadsTimePositionAndCamera()
        {
            byte[] photo = TestMedia.Photo("2024:06:01 14:30:00", "+02:00", (59.3293, -18.0686), "Canon", "EOS R5");

            ContentMetadata metadata = Read(photo);

            Assert.Equal(new DateTimeOffset(2024, 6, 1, 14, 30, 0, TimeSpan.FromHours(2)), metadata.TakenAt);
            Assert.Equal(59.3293, metadata.Latitude!.Value, 5);
            Assert.Equal(-18.0686, metadata.Longitude!.Value, 5);
            Assert.Equal("Canon EOS R5", metadata.Camera);
            Assert.Equal((null, null, null), (metadata.DurationSeconds, metadata.PageCount, metadata.Title));
        }

        [Fact]
        public void Read_PhotoWithoutAnOffset_KeepsTheClockTimeAsUtc()
        {
            ContentMetadata metadata = Read(TestMedia.Photo("2024:06:01 14:30:00"));

            Assert.Equal(new DateTimeOffset(2024, 6, 1, 14, 30, 0, TimeSpan.Zero), metadata.TakenAt);
        }

        [Fact]
        public void Read_ModelNamingTheMake_KeepsTheModelAlone()
        {
            ContentMetadata metadata = Read(TestMedia.Photo(make: "Canon", model: "Canon EOS R5"));

            Assert.Equal("Canon EOS R5", metadata.Camera);
        }

        [Fact]
        public void Read_PositionAtZeroZero_HasNoPosition()
        {
            ContentMetadata metadata = Read(TestMedia.Photo(position: (0, 0)));

            Assert.Equal((null, null), (metadata.Latitude, metadata.Longitude));
        }

        [Fact]
        public void Read_CameraTextWithControlCharactersAndPadding_CleansIt()
        {
            ContentMetadata metadata = Read(TestMedia.Photo(make: "  Apple\t", model: "iPhone\n15  "));

            Assert.Equal("Apple iPhone 15", metadata.Camera);
        }

        [Fact]
        public void Read_PhotoWithOnlyADigitizedTime_TakesThatTime()
        {
            ContentMetadata metadata = Read(TestMedia.Photo(digitized: "2024:06:01 15:45:10"));

            Assert.Equal(new DateTimeOffset(2024, 6, 1, 15, 45, 10, TimeSpan.Zero), metadata.TakenAt);
        }

        [Fact]
        public void Read_PhotoWithBothTimes_TakesTheOriginal()
        {
            ContentMetadata metadata = Read(TestMedia.Photo(taken: "2024:06:01 14:30:00", digitized: "2024:06:02 08:00:00"));

            Assert.Equal(new DateTimeOffset(2024, 6, 1, 14, 30, 0, TimeSpan.Zero), metadata.TakenAt);
        }

        [Fact]
        public void Read_MovieFromAnAndroidPhone_ReadsItsCamera()
        {
            ContentMetadata metadata = Read(TestMedia.Movie(created: null, TimeSpan.FromSeconds(3), androidManufacturer: "Google", androidModel: "Pixel 8"));

            Assert.Equal("Google Pixel 8", metadata.Camera);
        }

        [Fact]
        public void Read_PositionOffTheEarth_HasNoPosition()
        {
            ContentMetadata metadata = Read(TestMedia.Movie(created: null, TimeSpan.FromSeconds(3), location: "+95.0000+018.0686/"));

            Assert.Equal((null, null), (metadata.Latitude, metadata.Longitude));
        }

        [Fact]
        public void Read_MovieHeaderTimeZero_HasNoTime()
        {
            ContentMetadata metadata = Read(TestMedia.Movie(created: null, TimeSpan.FromSeconds(3)));

            Assert.Null(metadata.TakenAt);
        }

        [Fact]
        public void Read_Pdf_ReadsItsTitleAndPages()
        {
            ContentMetadata metadata = Read(TestMedia.Pdf(pages: 3, title: "Quarterly report"));

            Assert.Equal(("Quarterly report", 3), (metadata.Title, metadata.PageCount));
            Assert.Equal((null, null, null), (metadata.TakenAt, metadata.Camera, metadata.DurationSeconds));
        }

        [Fact]
        public void Read_PdfWithAVeryLongTitle_CutsIt()
        {
            ContentMetadata metadata = Read(TestMedia.Pdf(pages: 1, title: new string('t', MetadataReader.MaxTextLength + 50)));

            Assert.Equal(new string('t', MetadataReader.MaxTextLength), metadata.Title);
        }

        [Fact]
        public void Read_TitleWithAnEmojiAcrossTheLimit_CutsBeforeItWhole()
        {
            // A family emoji is seven code points and one character; placed to straddle the limit, it goes whole.
            const string Family = "\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466";
            string title = new string('t', MetadataReader.MaxTextLength - 3) + Family + "tail";

            ContentMetadata metadata = Read(TestMedia.Pdf(pages: 1, title: title));

            Assert.Equal(new string('t', MetadataReader.MaxTextLength - 3), metadata.Title);
        }

        [Fact]
        public void Read_TitleOfEmojiUpToTheLimit_KeepsAsManyCodePointsAsTheDatabaseAllows()
        {
            // Each is two UTF-16 units but one code point, as SQLite's length() counts it.
            string title = string.Concat(Enumerable.Repeat("\U0001F600", MetadataReader.MaxTextLength + 1));

            ContentMetadata metadata = Read(TestMedia.Pdf(pages: 1, title: title));

            Assert.Equal(MetadataReader.MaxTextLength, metadata.Title!.EnumerateRunes().Count());
        }

        [Fact]
        public void Read_ContentsOfNoKnownKind_FindsNothing()
        {
            Assert.Equal(ContentMetadata.None, Read(Encoding.UTF8.GetBytes("just some text, not a photo")));
            Assert.Equal(ContentMetadata.None, Read(Array.Empty<byte>()));
        }

        [Fact]
        public void Read_DamagedPdf_Throws()
        {
            Assert.ThrowsAny<Exception>(() => Read(Encoding.ASCII.GetBytes("%PDF-1.7\nthis is no PDF past its header")));
        }

        private static ContentMetadata Read(byte[] contents)
        {
            using MemoryStream stream = new MemoryStream(contents);
            return MetadataReader.Read(stream);
        }
    }
}
