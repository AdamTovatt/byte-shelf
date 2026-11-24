using System.Text.Json;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon.Tests
{
    public class PluginFieldTests
    {
        public static TheoryData<PluginFieldKind, string, PluginFieldValue> Taken => new TheoryData<PluginFieldKind, string, PluginFieldValue>
        {
            { PluginFieldKind.Text, "\"x\"", PluginFieldValue.OfText("x") },
            { PluginFieldKind.Text, "\"\"", PluginFieldValue.OfText(string.Empty) },
            { PluginFieldKind.Number, "-1.5e3", PluginFieldValue.OfNumber(-1500) },
            { PluginFieldKind.Flag, "false", PluginFieldValue.OfFlag(false) },
            { PluginFieldKind.Flag, "true", PluginFieldValue.OfFlag(true) },
            { PluginFieldKind.Date, "\"2026-03-01T14:30:00Z\"", PluginFieldValue.OfDate(new DateTimeOffset(2026, 3, 1, 14, 30, 0, TimeSpan.Zero)) },
            { PluginFieldKind.Date, "\"2026-03-01T14:30+02:00\"", PluginFieldValue.OfDate(new DateTimeOffset(2026, 3, 1, 12, 30, 0, TimeSpan.Zero)) },
        };

        [Theory]
        [InlineData("rating")]
        [InlineData("Alt-text_2")]
        public void Create_NameOfLettersDigitsHyphensAndUnderscores_IsAllowed(string name)
        {
            Assert.Equal(name, Field(name, PluginFieldKind.Text).Name);
        }

        [Theory]
        [InlineData("2nd")]
        [InlineData("a.b")]
        [InlineData("a b")]
        [InlineData("a~")]
        [InlineData("å")]
        [InlineData("a\n")]
        [InlineData("")]
        public void Create_AnyOtherName_IsRefused(string name)
        {
            Assert.Throws<ArgumentException>(() => Field(name, PluginFieldKind.Text));
        }

        [Fact]
        public void Create_NameOfMoreThan64Characters_IsRefused()
        {
            Assert.Equal(64, Field(new string('a', 64), PluginFieldKind.Text).Name.Length);
            Assert.Throws<ArgumentException>(() => Field(new string('a', 65), PluginFieldKind.Text));
        }

        [Fact]
        public void Create_BlankLabel_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => new PluginField("rating", " ", PluginFieldKind.Number, PluginDocumentSubject.File));
        }

        [Theory]
        [InlineData("a")]
        [InlineData("test-plugin-2")]
        [InlineData("2")]
        public void Create_IdOfLowercaseLettersDigitsAndHyphens_IsAllowed(string id)
        {
            Assert.Equal(id, new PluginDeclaration(id, "P").Id);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        [InlineData("A")]
        [InlineData("a.b")]
        [InlineData("a_b")]
        [InlineData("å")]
        [InlineData("a\n")]
        public void Create_AnyOtherId_IsRefused(string? id)
        {
            Assert.Throws<ArgumentException>(() => new PluginDeclaration(id!, "P"));
            Assert.False(PluginDeclaration.IsId(id));
        }

        [Fact]
        public void Create_PluginWithABlankName_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => new PluginDeclaration("p", " "));
        }

        [Fact]
        public void Create_TwoPluginsOfOneId_AreRefused()
        {
            Assert.Throws<ArgumentException>(() => new InstalledPlugins([new PluginDeclaration("p", "P"), new PluginDeclaration("p", "Q")]));
        }

        [Fact]
        public void Create_SearchedFieldThatIsNotText_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => new PluginField("rating", "Rating", PluginFieldKind.Number, PluginDocumentSubject.File, searched: true));
        }

        [Fact]
        public void Create_TwoFieldsOfOneName_AreRefused()
        {
            Assert.Throws<ArgumentException>(() => new PluginDeclaration(
                "p", "P", [Field("a", PluginFieldKind.Text), new PluginField("a", "A", PluginFieldKind.Flag, PluginDocumentSubject.Contents)]));
        }

        [Theory]
        [MemberData(nameof(Taken))]
        public void Of_ValueOfTheFieldsKind_IsTaken(PluginFieldKind kind, string json, PluginFieldValue taken)
        {
            Assert.Equal(taken, Of(Field("a", kind), $"{{\"a\":{json}}}"));
        }

        [Theory]
        [InlineData(PluginFieldKind.Text, "5")]
        [InlineData(PluginFieldKind.Text, "null")]
        [InlineData(PluginFieldKind.Text, "[\"x\"]")]
        [InlineData(PluginFieldKind.Number, "1e999")]
        [InlineData(PluginFieldKind.Number, "\"5\"")]
        [InlineData(PluginFieldKind.Flag, "0")]
        [InlineData(PluginFieldKind.Date, "\"2026-03-01T14:30:00\"")]
        [InlineData(PluginFieldKind.Date, "\"2026-03-01\"")]
        [InlineData(PluginFieldKind.Date, "\"2026-13-01T14:30:00Z\"")]
        [InlineData(PluginFieldKind.Date, "\"1 March 2026 14:30 +00:00\"")]
        [InlineData(PluginFieldKind.Date, "1772375400")]
        public void Of_AnyOtherValue_IsIgnored(PluginFieldKind kind, string json)
        {
            Assert.Null(Of(Field("a", kind), $"{{\"a\":{json}}}"));
        }

        [Fact]
        public void Of_Date_IsInUtc()
        {
            Assert.Equal(TimeSpan.Zero, Of(Field("a", PluginFieldKind.Date), "{\"a\":\"2026-03-01T14:30:00+02:00\"}")?.Date?.Offset);
        }

        [Fact]
        public void Of_NoDocumentOrNoSuchProperty_HasNone()
        {
            PluginField field = Field("a", PluginFieldKind.Text);

            Assert.Null(PluginFieldValue.Of(field, null));
            Assert.Null(Of(field, "{\"b\":{\"a\":\"x\"}}"));
        }

        [Fact]
        public void FoldedText_LongText_IsItsFirst1000CodePoints()
        {
            PluginFieldValue value = PluginFieldValue.OfText(string.Concat(Enumerable.Repeat("🎈", 999)) + "ÅX");

            Assert.EndsWith("🎈å", value.FoldedText(), StringComparison.Ordinal);
        }

        [Fact]
        public void FitsListings_Text_IsMeasuredInUtf8()
        {
            Assert.True(PluginFieldValue.OfText(new string('å', 128)).FitsListings());
            Assert.False(PluginFieldValue.OfText(new string('å', 128) + "x").FitsListings());
            Assert.True(PluginFieldValue.OfNumber(1).FitsListings());
        }

        [Fact]
        public void Json_Value_HoldsOnlyTheOnePartItHas()
        {
            string json = JsonSerializer.Serialize(PluginFieldValue.OfNumber(4), ByteShelfJsonContext.Default.PluginFieldValue);

            Assert.Equal("{\"number\":4}", json);
            Assert.Equal(PluginFieldValue.OfNumber(4), JsonSerializer.Deserialize(json, ByteShelfJsonContext.Default.PluginFieldValue));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"number\":4,\"flag\":false}")]
        public void Json_ValueOfNoPartOrOfTwo_IsRefused(string json)
        {
            Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize(json, ByteShelfJsonContext.Default.PluginFieldValue));
        }

        [Fact]
        public void Json_FileMetadataOfAServerWithoutPlugins_HasNone()
        {
            FileMetadata? metadata = JsonSerializer.Deserialize("{\"readable\":false}", ByteShelfJsonContext.Default.FileMetadata);

            Assert.Empty(metadata!.Plugins);
        }

        private static PluginFieldValue? Of(PluginField field, string document)
        {
            return PluginFieldValue.Of(field, PluginDocument.Parse(document).ToElement());
        }

        private static PluginField Field(string name, PluginFieldKind kind)
        {
            return new PluginField(name, "A field", kind, PluginDocumentSubject.File);
        }
    }
}
