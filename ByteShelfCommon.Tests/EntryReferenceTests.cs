namespace ByteShelfCommon.Tests
{
    public class EntryReferenceTests
    {
        private static readonly Guid SomeId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        [Fact]
        public void From_Path_NormalizesIt()
        {
            EntryReference reference = EntryReference.From("a/b/", null);

            Assert.Equal(("/a/b", (Guid?)null), (reference.Path, reference.Id));
        }

        [Fact]
        public void From_Id_KeepsIt()
        {
            EntryReference reference = EntryReference.From(null, SomeId);

            Assert.Equal(((string?)null, (Guid?)SomeId), (reference.Path, reference.Id));
        }

        [Fact]
        public void From_PathAndId_Throws()
        {
            Assert.Throws<ArgumentException>(() => EntryReference.From("/a", SomeId));
        }

        [Fact]
        public void From_Neither_Throws()
        {
            Assert.Throws<ArgumentException>(() => EntryReference.From(null, null));
        }

        [Fact]
        public void ToQuery_Path_EscapesIt()
        {
            Assert.Equal("path=%2Fa%20b%2Fc%26d%3D%23.txt", EntryReference.ForPath("/a b/c&d=#.txt").ToQuery());
        }

        [Fact]
        public void ToQuery_Id_NamesIt()
        {
            Assert.Equal("id=6f9619ff-8b86-d011-b42d-00c04fc964ff", EntryReference.ForId(SomeId).ToQuery());
        }

        [Fact]
        public void ImplicitConversions_TakeAPathOrAnId()
        {
            EntryReference byPath = "a.txt";
            EntryReference byId = SomeId;

            Assert.Equal(("/a.txt", (Guid?)SomeId), (byPath.Path, byId.Id));
        }

        [Fact]
        public void ImplicitConversion_PathNotAllowed_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                EntryReference reference = "/a/../b";
                return reference;
            });
        }
    }
}
