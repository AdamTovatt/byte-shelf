using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Metadata
{
    /// <summary>A video's time is the same instant whatever the server's clock is set to.</summary>
    [Collection(nameof(LocalTimeZoneCollection))]
    public class MetadataReaderTimeZoneTests
    {
        [Fact]
        public void Read_MovieWithAppleMetadataOnAServerAwayFromUtc_ReadsItsLengthTimePlaceAndCamera()
        {
            byte[] movie = TestMedia.Movie(
                new DateTimeOffset(2024, 6, 1, 9, 0, 0, TimeSpan.Zero),
                TimeSpan.FromSeconds(90.5),
                location: "+59.3293+018.0686+012.000/",
                creationDate: "2024-06-01T14:00:00+0200",
                make: "Apple",
                model: "iPhone 15 Pro");

            ContentMetadata metadata;
            using (LocalTimeZone.Set("Europe/Stockholm"))
            {
                metadata = Read(movie);
            }

            Assert.Equal(90.5, metadata.DurationSeconds);
            Assert.Equal(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero), metadata.TakenAt);
            Assert.Equal((59.3293, 18.0686), (metadata.Latitude, metadata.Longitude));
            Assert.Equal("Apple iPhone 15 Pro", metadata.Camera);
        }

        [Fact]
        public void Read_MovieWithOnlyItsHeaderOnAServerAwayFromUtc_TakesTheHeadersTime()
        {
            byte[] movie = TestMedia.Movie(new DateTimeOffset(2024, 6, 1, 9, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(2));

            ContentMetadata metadata;
            using (LocalTimeZone.Set("America/New_York"))
            {
                metadata = Read(movie);
            }

            Assert.Equal(new DateTimeOffset(2024, 6, 1, 9, 0, 0, TimeSpan.Zero), metadata.TakenAt);
            Assert.Equal(120, metadata.DurationSeconds);
        }

        private static ContentMetadata Read(byte[] contents)
        {
            using MemoryStream stream = new MemoryStream(contents);
            return MetadataReader.Read(stream);
        }
    }
}
