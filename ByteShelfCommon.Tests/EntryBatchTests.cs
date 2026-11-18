namespace ByteShelfCommon.Tests
{
    /// <summary>The two batch requests' forms and their rules (<see cref="EntryBatch"/>).</summary>
    public class EntryBatchTests
    {
        [Fact]
        public void MoveNormalize_Moves_KeepsTheirOrderWithEveryPathNormalized()
        {
            IReadOnlyList<EntryMove> moves = new MoveEntriesRequest
            {
                Moves = new[] { new EntryMove { Path = "b/", ToPath = "c" }, new EntryMove { Path = "/a", ToPath = "/b" } },
            }.Normalize();

            Assert.Equal(new[] { ("/b", "/c"), ("/a", "/b") }, moves.Select(move => (move.Path, move.ToPath)));
        }

        [Theory]
        [InlineData("/archive/", "/archive/a.txt")]
        [InlineData("/", "/a.txt")]
        public void MoveNormalize_PathsIntoADirectory_MovesEachToItsNameThere(string intoDirectory, string toPath)
        {
            EntryMove move = Assert.Single(new MoveEntriesRequest { Paths = new[] { "/notes/a.txt" }, IntoDirectory = intoDirectory }.Normalize());

            Assert.Equal(("/notes/a.txt", toPath), (move.Path, move.ToPath));
        }

        [Fact]
        public void MoveNormalize_IntoADirectoryPastTheLongestPath_LeavesItToThatMove()
        {
            // Short enough for /a.txt under it, too long for the long name.
            string deep = string.Concat(Enumerable.Repeat("/" + new string('n', ShelfPath.MaxNameLength), (ShelfPath.MaxPathLength / (ShelfPath.MaxNameLength + 1)) - 1)) + "/xx";
            string name = new string('m', ShelfPath.MaxNameLength);

            EntryMove move = Assert.Single(new MoveEntriesRequest { Paths = new[] { "/" + name }, IntoDirectory = deep }.Normalize());

            Assert.Equal(deep + "/" + name, move.ToPath);
        }

        [Fact]
        public void MoveNormalize_MaxItems_IsAllowed()
        {
            Assert.Equal(EntryBatch.MaxItems, new MoveEntriesRequest { Paths = Enumerable.Repeat("/a", EntryBatch.MaxItems).ToList(), IntoDirectory = "/b" }.Normalize().Count);
        }

        public static TheoryData<MoveEntriesRequest, string> RefusedMoves => new TheoryData<MoveEntriesRequest, string>
        {
            { new MoveEntriesRequest(), "Give moves, or paths with intoDirectory." },
            { new MoveEntriesRequest { Paths = new[] { "/a" } }, "Give moves, or paths with intoDirectory." },
            { new MoveEntriesRequest { Moves = new[] { new EntryMove { Path = "/a", ToPath = "/b" } }, IntoDirectory = "/c" }, "Give moves, or paths with intoDirectory, not both." },
            { new MoveEntriesRequest { Moves = Array.Empty<EntryMove>() }, $"Give 1 to {EntryBatch.MaxItems} moves; this gives 0." },
            { new MoveEntriesRequest { Paths = Enumerable.Repeat("/a", EntryBatch.MaxItems + 1).ToList(), IntoDirectory = "/b" }, $"Give 1 to {EntryBatch.MaxItems} paths; this gives {EntryBatch.MaxItems + 1}." },
            { new MoveEntriesRequest { Moves = new EntryMove[] { null! } }, "One of the moves is null." },
            { new MoveEntriesRequest { Paths = new[] { "/" }, IntoDirectory = "/b" }, "The root directory cannot be moved." },
        };

        [Theory]
        [MemberData(nameof(RefusedMoves))]
        public void MoveNormalize_NotOneFormOfAllowedItems_Throws(MoveEntriesRequest request, string message)
        {
            Assert.Equal(message, Assert.Throws<ArgumentException>(() => request.Normalize()).Message);
        }

        [Fact]
        public void DescriptionsNormalize_OneForSeveralPaths_GivesEachTheNormalizedDescription()
        {
            IReadOnlyList<DescriptionChange> changes = new SetDescriptionsRequest { Paths = new[] { "b", "/a" }, Description = "  Signed  " }.Normalize();

            Assert.Equal(new[] { ("/b", (string?)"Signed"), ("/a", "Signed") }, changes.Select(change => (change.Path, change.Description)));
        }

        [Fact]
        public void DescriptionsNormalize_PathsWithoutADescription_RemovesTheirs()
        {
            Assert.Null(Assert.Single(new SetDescriptionsRequest { Paths = new[] { "/a" }, Description = "  " }.Normalize()).Description);
        }

        [Fact]
        public void DescriptionsNormalize_EachItsOwn_KeepsTheirOrder()
        {
            IReadOnlyList<DescriptionChange> changes = new SetDescriptionsRequest
            {
                Descriptions = new[] { new DescriptionChange { Path = "/b", Description = "B" }, new DescriptionChange { Path = "/a" } },
            }.Normalize();

            Assert.Equal(new[] { ("/b", (string?)"B"), ("/a", null) }, changes.Select(change => (change.Path, change.Description)));
        }

        public static TheoryData<SetDescriptionsRequest, string> RefusedDescriptions => new TheoryData<SetDescriptionsRequest, string>
        {
            { new SetDescriptionsRequest(), "Give descriptions, or paths with one description." },
            { new SetDescriptionsRequest { Description = "a" }, "Give descriptions, or paths with one description." },
            { new SetDescriptionsRequest { Descriptions = new[] { new DescriptionChange { Path = "/a" } }, Paths = new[] { "/b" } }, "Give descriptions, or paths with one description, not both." },
            { new SetDescriptionsRequest { Paths = Array.Empty<string>() }, $"Give 1 to {EntryBatch.MaxItems} paths; this gives 0." },
            { new SetDescriptionsRequest { Descriptions = new DescriptionChange[] { null! } }, "One of the descriptions is null." },
        };

        [Theory]
        [MemberData(nameof(RefusedDescriptions))]
        public void DescriptionsNormalize_NotOneFormOfAllowedItems_Throws(SetDescriptionsRequest request, string message)
        {
            Assert.Equal(message, Assert.Throws<ArgumentException>(() => request.Normalize()).Message);
        }
    }
}
