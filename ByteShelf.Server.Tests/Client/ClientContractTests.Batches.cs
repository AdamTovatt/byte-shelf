using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Client
{
    /// <summary>Batches of moves and descriptions, the same in both clients.</summary>
    public abstract partial class ClientContractTests
    {
        [Fact]
        public async Task MoveAsync_Pairs_MovesFilesAndDirectoriesAnsweringEachWhereItIsNow()
        {
            ShelfFile file = await UploadTextAsync("/a.txt", "a");
            await UploadTextAsync("/photos/b.jpg", "b");

            IReadOnlyList<EntryMoveResult> results = await Shelf.MoveAsync(Moves(("/a.txt", "/archive/a.txt"), ("/photos", "/archive/photos")));

            Assert.Equal((file.Id, "/archive/a.txt"), (results[0].File!.Id, results[0].File!.Path));
            Assert.Equal(("/photos", "/archive/photos", "/archive/photos"), (results[1].Path, results[1].ToPath, results[1].Directory!.Path));
            Assert.All(results, result => Assert.Null(result.Error));
            Assert.Equal("b", await ReadTextAsync("/archive/photos/b.jpg"));
        }

        [Fact]
        public async Task MoveAsync_ALaterMove_UsesAPathAnEarlierOneMade()
        {
            await UploadTextAsync("/a.txt", "a");

            IReadOnlyList<EntryMoveResult> results = await Shelf.MoveAsync(Moves(("/a.txt", "/b.txt"), ("/b.txt", "/c.txt")));

            Assert.All(results, result => Assert.Null(result.Error));
            Assert.Equal("a", await ReadTextAsync("/c.txt"));
        }

        [Fact]
        public async Task MoveAsync_ARefusedMove_IsItsResultWithItsCodeAndTheOthersAreDone()
        {
            await UploadTextAsync("/a.txt", "a");
            await UploadTextAsync("/b.txt", "b");
            await UploadTextAsync("/taken/b.txt", "taken");
            await UploadTextAsync("/c.txt", "c");

            IReadOnlyList<EntryMoveResult> results = await Shelf.MoveAsync(new MoveEntriesRequest { Paths = new[] { "/a.txt", "/b.txt", "/missing", "/c.txt" }, IntoDirectory = "/taken" });

            Assert.Equal(
                new[] { (string?)null, ErrorCodes.Conflict, ErrorCodes.NotFound, null },
                results.Select(result => result.Error?.Code));
            Assert.Equal(new[] { "/a.txt", "/b.txt", "/missing", "/c.txt" }, results.Select(result => result.Path));
            Assert.Equal(("a", "b", "c"), (await ReadTextAsync("/taken/a.txt"), await ReadTextAsync("/b.txt"), await ReadTextAsync("/taken/c.txt")));
        }

        [Fact]
        public async Task MoveAsync_ARefusedMove_LeavesNoDirectoryItsDestinationNeeded()
        {
            await UploadTextAsync("/a.txt", "a");
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());

            // The destination's directories are made before a plain file is refused under an encrypted one.
            IReadOnlyList<EntryMoveResult> results = await Shelf.MoveAsync(Moves(("/a.txt", "/secret/new/a.txt")));

            Assert.Equal(ErrorCodes.Conflict, Assert.Single(results).Error?.Code);
            Assert.Equal(ErrorCodes.NotFound, (await RefusalOf(Shelf.ListAsync("/secret/new"))).Code);
        }

        [Fact]
        public async Task MoveAsync_ARefusedMove_KeepsTheDirectoriesAnEarlierMoveMade()
        {
            await UploadTextAsync("/a.txt", "a");
            await UploadTextAsync("/b.txt", "b");
            await Shelf.CreateDirectoryAsync("/secret");
            await Shelf.EncryptDirectoryAsync("/secret", await CheckAsync());

            IReadOnlyList<EntryMoveResult> results = await Shelf.MoveAsync(Moves(("/a.txt", "/new/a.txt"), ("/b.txt", "/secret/new/b.txt")));

            Assert.Equal(new[] { null, ErrorCodes.Conflict }, results.Select(result => result.Error?.Code));
            Assert.Equal("a", await ReadTextAsync("/new/a.txt"));
        }

        [Fact]
        public async Task MoveAsync_TheRootOrIntoItself_IsRefusedAsInvalid()
        {
            await Shelf.CreateDirectoryAsync("/a");

            IReadOnlyList<EntryMoveResult> results = await Shelf.MoveAsync(Moves(("/", "/b"), ("/a", "/a/b")));

            Assert.Equal(new[] { ErrorCodes.InvalidRequest, ErrorCodes.InvalidRequest }, results.Select(result => result.Error?.Code));
        }

        [Fact]
        public async Task MoveAsync_IntoADirectoryPastTheLongestPath_RefusesThatMoveAlone()
        {
            string longName = new string('n', ShelfPath.MaxNameLength);

            // Short enough for /a.txt under it, too long for the long name.
            string deep = string.Concat(Enumerable.Repeat("/" + new string('d', ShelfPath.MaxNameLength), (ShelfPath.MaxPathLength / (longName.Length + 1)) - 1)) + "/xx";
            await Shelf.CreateDirectoryAsync(deep);
            await UploadTextAsync("/" + longName, "long");
            await UploadTextAsync("/a.txt", "a");

            IReadOnlyList<EntryMoveResult> results = await Shelf.MoveAsync(new MoveEntriesRequest { Paths = new[] { "/" + longName, "/a.txt" }, IntoDirectory = deep });

            Assert.Equal(new[] { ErrorCodes.InvalidRequest, null }, results.Select(result => result.Error?.Code));
            Assert.Equal("a", await ReadTextAsync(deep + "/a.txt"));
        }

        [Theory]
        [MemberData(nameof(RefusedMoveRequests))]
        public async Task MoveAsync_ARequestTheRulesRefuse_ThrowsBeforeMovingAnything(MoveEntriesRequest request)
        {
            await UploadTextAsync("/a.txt", "a");

            await Assert.ThrowsAsync<ArgumentException>(() => Shelf.MoveAsync(request));

            Assert.Equal("a", await ReadTextAsync("/a.txt"));
        }

        public static TheoryData<MoveEntriesRequest> RefusedMoveRequests => new TheoryData<MoveEntriesRequest>
        {
            new MoveEntriesRequest(),
            new MoveEntriesRequest { Moves = Array.Empty<EntryMove>() },
            new MoveEntriesRequest { Moves = Moves(("/a.txt", "/b.txt")).Moves, Paths = new[] { "/a.txt" }, IntoDirectory = "/c" },
            new MoveEntriesRequest { Paths = new[] { "/a.txt" } },
            new MoveEntriesRequest { Paths = new[] { "/a.txt", "/" }, IntoDirectory = "/c" },
            new MoveEntriesRequest { Paths = Enumerable.Repeat("/a.txt", EntryBatch.MaxItems + 1).ToList(), IntoDirectory = "/c" },
        };

        [Fact]
        public async Task SetDescriptionsAsync_EachItsOwn_SetsAndRemovesThemInOrder()
        {
            await Shelf.UploadAsync("/a.txt", Text("a"), new UploadOptions { Description = "Draft" });

            IReadOnlyList<DescriptionResult> results = await Shelf.SetDescriptionsAsync(new SetDescriptionsRequest
            {
                Descriptions = new[]
                {
                    new DescriptionChange { Path = "/a.txt", Description = "  Signed copy  " },
                    new DescriptionChange { Path = "/missing.txt", Description = "Lost" },
                    new DescriptionChange { Path = "/a.txt", Description = "   " },
                },
            });

            Assert.Equal(("Signed copy", ErrorCodes.NotFound), (results[0].File!.Description, results[1].Error!.Code));
            Assert.Equal(("/a.txt", (string?)null), (results[2].Path, results[2].File!.Description));
            Assert.Null((await Shelf.GetFileAsync("/a.txt")).Description);
        }

        [Fact]
        public async Task SetDescriptionsAsync_OneForSeveralPaths_SetsItOnEachFileAndRefusesADirectory()
        {
            await UploadTextAsync("/a.txt", "a");
            await UploadTextAsync("/d/b.txt", "b");

            IReadOnlyList<DescriptionResult> results = await Shelf.SetDescriptionsAsync(new SetDescriptionsRequest { Paths = new[] { "/a.txt", "/d", "/d/b.txt" }, Description = "Signed copy" });

            Assert.Equal(new[] { null, ErrorCodes.NotFound, null }, results.Select(result => result.Error?.Code));
            Assert.Equal("Signed copy", (await Shelf.GetFileAsync("/d/b.txt")).Description);
        }

        [Fact]
        public async Task SetDescriptionsAsync_ADescriptionTooLong_ThrowsBeforeChangingAnything()
        {
            await Shelf.UploadAsync("/a.txt", Text("a"), new UploadOptions { Description = "Kept" });
            SetDescriptionsRequest request = new SetDescriptionsRequest
            {
                Descriptions = new[]
                {
                    new DescriptionChange { Path = "/a.txt" },
                    new DescriptionChange { Path = "/a.txt", Description = new string('x', FileDescription.MaxLength + 1) },
                },
            };

            await Assert.ThrowsAsync<ArgumentException>(() => Shelf.SetDescriptionsAsync(request));

            Assert.Equal("Kept", (await Shelf.GetFileAsync("/a.txt")).Description);
        }

        private static MoveEntriesRequest Moves(params (string Path, string ToPath)[] moves)
        {
            return new MoveEntriesRequest { Moves = moves.Select(move => new EntryMove { Path = move.Path, ToPath = move.ToPath }).ToList() };
        }
    }
}
