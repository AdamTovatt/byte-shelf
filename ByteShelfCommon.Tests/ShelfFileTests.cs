using System.Text.Json;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon.Tests
{
    public class ShelfFileTests
    {
        private const string Record = """
            {"id":"6f1c1e4e-3c55-4c3b-9f1e-4f7a0e8b2d11","shelfId":"6f1c1e4e-3c55-4c3b-9f1e-4f7a0e8b2d12",
            "directoryId":"6f1c1e4e-3c55-4c3b-9f1e-4f7a0e8b2d13","name":"a.mp4","path":"/a.mp4","sizeBytes":1,"contentType":"video/mp4",
            "sha256":"abc","createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-01T00:00:00+00:00","encrypted":false,
            "derived":{DERIVED}}
            """;

        [Fact]
        public void Derived_AKindThisClientDoesNotKnow_IsLeftOutAndTheRestRead()
        {
            ShelfFile file = Read("""{"thumbnail":"1","hologram":"2","Preview":"3","1":"4","thumbnail, preview":"5","later":{"of":[1,null]},"other":7}""");

            Assert.Equal(new Dictionary<DerivedKind, string> { [DerivedKind.Thumbnail] = "1", [DerivedKind.Preview] = "3" }, file.Derived);
        }

        /// <summary>Read by camelCase names alone (<see cref="Derived_AKindThisClientDoesNotKnow_IsLeftOutAndTheRestRead"/>), so written by them too.</summary>
        [Fact]
        public void Derived_EveryKind_IsReadByItsCamelCaseNameAndWrittenBack()
        {
            Dictionary<DerivedKind, string> derived = Enum.GetValues<DerivedKind>().ToDictionary(kind => kind, kind => ((int)kind).ToString());
            const string Written = "{\"thumbnail\":\"0\",\"preview\":\"1\",\"full\":\"2\",\"agent\":\"3\",\"stream\":\"4\"}";

            ShelfFile file = Read(Written);

            Assert.Equal(derived, file.Derived);
            Assert.Contains($"\"derived\":{Written}", JsonSerializer.Serialize(file, ByteShelfJsonContext.Default.ShelfFile));
        }

        [Theory]
        [InlineData("null")]
        [InlineData("{}")]
        public void Derived_NoneOrEmpty_IsReadSo(string derived)
        {
            Assert.Equal(derived == "null" ? null : new Dictionary<DerivedKind, string>(), Read(derived).Derived);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("{\"thumbnail\":1}")]
        [InlineData("{\"thumbnail\":null}")]
        public void Derived_NotKindsWithTags_IsRefused(string derived)
        {
            Assert.Throws<JsonException>(() => Read(derived));
        }

        private static ShelfFile Read(string derived)
        {
            return JsonSerializer.Deserialize(Record.Replace("{DERIVED}", derived), ByteShelfJsonContext.Default.ShelfFile)!;
        }
    }
}
