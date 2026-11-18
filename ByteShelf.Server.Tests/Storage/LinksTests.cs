using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Repositories;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Storage
{
    /// <summary>
    /// Link rows: when a link stops being found, who sees and revokes which links, and what removes a
    /// link with what it names.
    /// </summary>
    public class LinksTests : ServiceTestBase
    {
        private Shelf _shelf = null!;
        private ShelfAccess _admin = null!;

        private DownloadLinks DownloadLinks => Services.GetRequiredService<DownloadLinks>();
        private UploadLinks UploadLinks => Services.GetRequiredService<UploadLinks>();
        private ILinkService LinkService => Services.GetRequiredService<ILinkService>();
        private IStorageMaintenance Maintenance => Services.GetRequiredService<IStorageMaintenance>();

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _shelf = await CreateShelfAsync("docs");
            _admin = await AccessAsync(ShelfCaller.ForUser(await CreateUserAsync("admin@byteshelf.test", UserRoles.Admin)));
        }

        [Fact]
        public async Task Find_UntilTheLinkRunsOut_AnswersItsRow()
        {
            ShelfFile file = await StoreTextAsync(_shelf, "/a.txt", "a");
            DownloadLink link = await DownloadLinks.CreateAsync(_admin, file.Id, TimeSpan.FromMinutes(10));

            Clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromTicks(1));
            FoundLink<DownloadLinkTarget>? before = await DownloadLinks.FindAsync(TokenOf(link.Url));
            Clock.Advance(TimeSpan.FromTicks(1));
            FoundLink<DownloadLinkTarget>? at = await DownloadLinks.FindAsync(TokenOf(link.Url));

            Assert.Equal((new DownloadLinkTarget(file.Id), _admin.Caller.Reference), (before?.Target, before?.Link.CreatedBy));
            Assert.Null(at);
        }

        [Fact]
        public async Task Find_UploadLink_AnswersWhatItStoresAndWho()
        {
            UploadLink link = await UploadLinks.CreateAsync(_admin, new CreateUploadLinkRequest { Path = "/räksmörgås/a.txt", Overwrite = true, Description = "  en beskrivning  " }.Normalize());

            FoundLink<UploadLinkTarget>? found = await UploadLinks.FindAsync(TokenOf(link.Url));

            Assert.Equal((new UploadLinkTarget("/räksmörgås/a.txt", true, "en beskrivning"), _admin.Caller.Reference), (found?.Target, found?.Link.CreatedBy));
        }

        [Fact]
        public async Task Find_DownloadLinksToken_IsNoUploadLink()
        {
            ShelfFile file = await StoreTextAsync(_shelf, "/a.txt", "a");
            DownloadLink link = await DownloadLinks.CreateAsync(_admin, file.Id, null);

            Assert.Null(await UploadLinks.FindAsync(TokenOf(link.Url)));
        }

        [Fact]
        public async Task List_CallerWhoMayWrite_SeesEveryLinkNewestFirst()
        {
            ShelfFile file = await StoreTextAsync(_shelf, "/a.txt", "a");
            ShelfAccess reader = await GrantedAsync("reader@byteshelf.test", ShelfAccessPermission.Read);
            ShelfAccess writer = await GrantedAsync("writer@byteshelf.test", ShelfAccessPermission.ReadWrite);
            DownloadLink readers = await DownloadLinks.CreateAsync(reader, file.Id, null);
            Clock.Advance(TimeSpan.FromSeconds(1));
            UploadLink writers = await UploadLinks.CreateAsync(writer, new CreateUploadLinkRequest { Path = "/b.txt", Overwrite = true, Description = "the report" }.Normalize());

            IReadOnlyList<ShelfLink> links = await LinkService.ListAsync(writer);

            Assert.Equal(
                new[]
                {
                    (writers.Id, ShelfLinkKind.Upload, "/b.txt", (Guid?)null, true, "the report", "writer", true),
                    (readers.Id, ShelfLinkKind.Download, "/a.txt", file.Id, false, (string?)null, "reader", false),
                },
                links.Select(link => (link.Id, link.Kind, link.Path!, link.FileId, link.Overwrite, link.Description, link.CreatedBy.Name, link.CreatedBy.IsCaller)));
        }

        [Fact]
        public async Task List_CallerWhoMayOnlyRead_SeesOnlyItsOwn()
        {
            ShelfFile file = await StoreTextAsync(_shelf, "/a.txt", "a");
            ShelfAccess reader = await GrantedAsync("reader@byteshelf.test", ShelfAccessPermission.Read);
            DownloadLink own = await DownloadLinks.CreateAsync(reader, file.Id, null);
            await DownloadLinks.CreateAsync(_admin, file.Id, null);

            IReadOnlyList<ShelfLink> links = await LinkService.ListAsync(reader);

            Assert.Equal(own.Id, Assert.Single(links).Id);
        }

        [Fact]
        public async Task List_AddOnlyKey_SeesOnlyItsOwnNamedAsTheKey()
        {
            ShelfAccess key = await KeyAccessAsync(ShelfKeyPermission.AddOnly);
            UploadLink own = await UploadLinks.CreateAsync(key, new CreateUploadLinkRequest { Path = "/a.txt" }.Normalize());
            await UploadLinks.CreateAsync(_admin, new CreateUploadLinkRequest { Path = "/b.txt" }.Normalize());

            ShelfLink listed = Assert.Single(await LinkService.ListAsync(key));

            Assert.Equal((own.Id, LinkMakerKind.Key, "backup job", true), (listed.Id, listed.CreatedBy.Kind, listed.CreatedBy.Name, listed.CreatedBy.IsCaller));
        }

        [Fact]
        public async Task List_LinkThatRanOut_IsLeftOut()
        {
            UploadLink link = await UploadLinks.CreateAsync(_admin, new CreateUploadLinkRequest { Path = "/a.txt", ExpiresInMinutes = 5 }.Normalize());

            Clock.Advance(TimeSpan.FromMinutes(5));

            Assert.Empty(await LinkService.ListAsync(_admin));
        }

        [Fact]
        public async Task List_FileMovedThenTrashed_GivesItsPathThenNone()
        {
            ShelfFile file = await StoreTextAsync(_shelf, "/a.txt", "a");
            await DownloadLinks.CreateAsync(_admin, file.Id, null);

            await FileService.MoveFileAsync(_shelf, EntryReference.ForId(file.Id), "/moved/deeper/b.txt");
            string? moved = Assert.Single(await LinkService.ListAsync(_admin)).Path;
            await FileService.DeleteFileAsync(_shelf, EntryReference.ForId(file.Id));
            ShelfLink trashed = Assert.Single(await LinkService.ListAsync(_admin));

            Assert.Equal(("/moved/deeper/b.txt", null, file.Id), (moved, trashed.Path, trashed.FileId));
        }

        [Fact]
        public async Task Revoke_OwnLink_EndsIt()
        {
            ShelfFile file = await StoreTextAsync(_shelf, "/a.txt", "a");
            ShelfAccess reader = await GrantedAsync("reader@byteshelf.test", ShelfAccessPermission.Read);
            DownloadLink link = await DownloadLinks.CreateAsync(reader, file.Id, null);

            await LinkService.RevokeAsync(reader, link.Id);

            Assert.Null(await DownloadLinks.FindAsync(TokenOf(link.Url)));
        }

        [Fact]
        public async Task Revoke_SomeoneElsesLinkByOneWhoMayOnlyRead_IsNotFoundAndKeepsIt()
        {
            ShelfFile file = await StoreTextAsync(_shelf, "/a.txt", "a");
            ShelfAccess reader = await GrantedAsync("reader@byteshelf.test", ShelfAccessPermission.Read);
            DownloadLink link = await DownloadLinks.CreateAsync(_admin, file.Id, null);

            await Assert.ThrowsAsync<NotFoundException>(() => LinkService.RevokeAsync(reader, link.Id));

            Assert.Equal(link.Id, (await DownloadLinks.FindAsync(TokenOf(link.Url)))?.Link.Id);
        }

        [Fact]
        public async Task Revoke_SomeoneElsesLinkByOneWhoMayWrite_EndsIt()
        {
            ShelfAccess writer = await GrantedAsync("writer@byteshelf.test", ShelfAccessPermission.ReadWrite);
            ShelfAccess key = await KeyAccessAsync(ShelfKeyPermission.AddOnly);
            UploadLink link = await UploadLinks.CreateAsync(key, new CreateUploadLinkRequest { Path = "/a.txt" }.Normalize());

            await LinkService.RevokeAsync(writer, link.Id);

            Assert.Null(await UploadLinks.FindAsync(TokenOf(link.Url)));
        }

        [Fact]
        public async Task Revoke_LinkOnAnotherShelf_IsNotFoundAndKeepsIt()
        {
            Shelf other = await CreateShelfAsync("other");
            UploadLink link = await UploadLinks.CreateAsync(await AccessAsync(_admin.Caller, other), new CreateUploadLinkRequest { Path = "/a.txt" }.Normalize());

            await Assert.ThrowsAsync<NotFoundException>(() => LinkService.RevokeAsync(_admin, link.Id));

            Assert.Equal(link.Id, (await UploadLinks.FindAsync(TokenOf(link.Url)))?.Link.Id);
        }

        [Fact]
        public async Task KeyDeleted_RemovesItsLinks()
        {
            ShelfAccess key = await KeyAccessAsync(ShelfKeyPermission.AddOnly);
            await UploadLinks.CreateAsync(key, new CreateUploadLinkRequest { Path = "/a.txt" }.Normalize());

            await KeyService.DeleteAsync(_shelf, key.Caller.Key!.Id);

            Assert.Empty(await LinkService.ListAsync(_admin));
        }

        [Fact]
        public async Task UserDeleted_RemovesTheirLinks()
        {
            ShelfAccess reader = await GrantedAsync("reader@byteshelf.test", ShelfAccessPermission.Read);
            ShelfFile file = await StoreTextAsync(_shelf, "/a.txt", "a");
            await DownloadLinks.CreateAsync(reader, file.Id, null);

            await Services.GetRequiredService<IUserService>().DeleteUserAsync(reader.Caller.UserId!.Value);

            Assert.Empty(await LinkService.ListAsync(_admin));
        }

        [Fact]
        public async Task FilePurged_RemovesItsLinks()
        {
            ShelfFile file = await StoreTextAsync(_shelf, "/a.txt", "a");
            await DownloadLinks.CreateAsync(_admin, file.Id, null);
            await FileService.DeleteFileAsync(_shelf, EntryReference.ForId(file.Id));

            await TrashService.EmptyAsync(_shelf, CancellationToken.None);

            Assert.Empty(await LinkService.ListAsync(_admin));
        }

        [Fact]
        public async Task ShelfDeleted_RemovesItsLinks()
        {
            Shelf other = await CreateShelfAsync("other");
            UploadLink link = await UploadLinks.CreateAsync(await AccessAsync(_admin.Caller, other), new CreateUploadLinkRequest { Path = "/a.txt" }.Normalize());

            await ShelfService.DeleteAsync(other);

            Assert.Null(await UploadLinks.FindAsync(TokenOf(link.Url)));
        }

        [Fact]
        public async Task Maintenance_RemovesTheLinksThatRanOutAndKeepsTheRest()
        {
            await UploadLinks.CreateAsync(_admin, new CreateUploadLinkRequest { Path = "/a.txt", ExpiresInMinutes = 5 }.Normalize());
            UploadLink later = await UploadLinks.CreateAsync(_admin, new CreateUploadLinkRequest { Path = "/b.txt", ExpiresInMinutes = 6 }.Normalize());
            Clock.Advance(TimeSpan.FromMinutes(5));

            StorageMaintenanceSummary summary = await Maintenance.RunAsync(Clock.GetUtcNow(), CancellationToken.None);

            Assert.Equal((1, later.Id), (summary.ExpiredLinks, Assert.Single(await LinkService.ListAsync(_admin)).Id));
        }

        private static string TokenOf(string url)
        {
            return url[(url.LastIndexOf('/') + 1)..];
        }

        private Task<ShelfAccess> AccessAsync(ShelfCaller caller, Shelf? shelf = null)
        {
            return AccessResolver.ResolveAsync(caller, shelf ?? _shelf);
        }

        /// <summary>A user granted <paramref name="permission"/>, displayed as the part of the email before the @, unlike the admin.</summary>
        private async Task<ShelfAccess> GrantedAsync(string email, ShelfAccessPermission permission)
        {
            User user = await Services.GetRequiredService<IUserService>().CreateUserAsync(email, email[..email.IndexOf('@')], "password-for-tests");
            await GrantService.SetAsync(_shelf, user.Id, permission);
            return await AccessAsync(ShelfCaller.ForUser(user));
        }

        private async Task<ShelfAccess> KeyAccessAsync(ShelfKeyPermission permission)
        {
            CreatedShelfKey created = await KeyService.CreateAsync(_shelf, "backup job", permission);
            ShelfKey key = (await Services.GetRequiredService<IShelfKeyRepository>().GetByIdAsync(created.Key.Id))!;
            return await AccessAsync(ShelfCaller.ForKey(key));
        }
    }
}
