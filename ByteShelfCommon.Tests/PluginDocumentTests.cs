using System.Text.Json;

namespace ByteShelfCommon.Tests
{
    public class PluginDocumentTests
    {
        [Fact]
        public void Parse_AnObject_KeepsItWithoutWhitespaceAndItsTextUnescaped()
        {
            PluginDocument document = PluginDocument.Parse("{ \"name\" : \"\\u00c5sa 日本\", \"n\": 1.50 }");

            Assert.Equal("{\"name\":\"Åsa 日本\",\"n\":1.50}", document.Json);
            Assert.Equal("Åsa 日本", document.ToElement().GetProperty("name").GetString());
        }

        [Theory]
        [InlineData("{\"t\":\"🎈\"}", "{\"t\":\"🎈\"}")]
        [InlineData("{\"t\":\"\\ud83c\\udf88\"}", "{\"t\":\"🎈\"}")]
        [InlineData("{\"t\":\"\\ue000\\u2028\\u007f\"}", "{\"t\":\"\ue000\u2028\u007f\"}")]
        [InlineData("{\"t\":\"\\u0022\\u005c\\u000a\\u0001\\u005cu0041\"}", "{\"t\":\"\\\"\\\\\\n\\u0001\\\\u0041\"}")]
        public void Parse_EscapedText_IsKeptEscapedOnlyWhereJsonRequiresIt(string given, string kept)
        {
            PluginDocument document = PluginDocument.Parse(given);

            Assert.Equal(kept, document.Json);
            Assert.Equal(document, PluginDocument.Parse(document.Json));
            using JsonDocument original = JsonDocument.Parse(given);
            Assert.Equal(original.RootElement.GetProperty("t").GetString(), document.ToElement().GetProperty("t").GetString());
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("null")]
        [InlineData("\"text\"")]
        [InlineData("{")]
        [InlineData("")]
        [InlineData("{\"a\":1,}")]
        [InlineData("{\"a\":\"\\ud800\"}")]
        public void Parse_NotAnObject_IsRefused(string json)
        {
            Assert.Throws<ArgumentException>(() => PluginDocument.Parse(json));
        }

        [Fact]
        public void Parse_Size_IsCountedInUtf8WithoutWhitespace()
        {
            // Around the text: {"t":""}. Four bytes a character.
            string fits = string.Concat(Enumerable.Repeat("🎈", (PluginDocument.MaxBytes - 8) / 4));

            Assert.Equal(PluginDocument.MaxBytes, System.Text.Encoding.UTF8.GetByteCount(PluginDocument.Parse($"{{ \"t\" : \"{fits}\" }}").Json));
            Assert.Throws<ArgumentException>(() => PluginDocument.Parse($"{{\"t\":\"{fits}x\"}}"));
        }

        [Fact]
        public void Parse_Depth_IsLimited()
        {
            PluginDocument deepest = PluginDocument.Parse(Nested(PluginDocument.MaxDepth));

            Assert.Equal(deepest, JsonSerializer.Deserialize<Holder>(JsonSerializer.Serialize(new Holder { Document = deepest }))!.Document);
            Assert.Throws<ArgumentException>(() => PluginDocument.Parse(Nested(PluginDocument.MaxDepth + 1)));
        }

        [Fact]
        public void Json_IsTheObjectItself()
        {
            string json = JsonSerializer.Serialize(new Holder { Document = PluginDocument.Parse("{\"rating\":5}") });

            Assert.Equal("{\"Document\":{\"rating\":5}}", json);
            Assert.Equal("{\"rating\":5}", JsonSerializer.Deserialize<Holder>(json)!.Document!.Json);
        }

        [Fact]
        public void Json_ThatIsNoDocument_IsNoJson()
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Holder>("{\"Document\":[1]}"));
        }

        private static string Nested(int depth)
        {
            return string.Concat(Enumerable.Repeat("{\"a\":", depth - 1)) + "{}" + new string('}', depth - 1);
        }

        private sealed class Holder
        {
            public PluginDocument? Document { get; set; }
        }
    }
}
