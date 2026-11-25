using System.Text;
using ByteShelf.Server.Access.Services;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.ChangeLog.Services;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// Runs the server's domains as the server wires them — the same registrations, on a root and
    /// database of the test's own, with a fake clock — so a test exercises the real files on disk and
    /// the real transactions rather than doubles of them.
    /// </summary>
    public abstract class ServiceTestBase : IAsyncLifetime
    {
        public const string AppBaseUrl = "https://shelf.test";

        /// <summary>The caller the service tests start uploads as; who starts one matters only at the API.</summary>
        protected const string Uploader = "user:00000000-0000-0000-0000-00000000000a";

        /// <summary>Contents the server takes as encrypted: it checks only that they start as an age file.</summary>
        protected static readonly byte[] AgeBytes = Encoding.ASCII.GetBytes("age-encryption.org/v1\nan encrypted file");

        /// <summary>Contents the server takes as encrypted and as plain.</summary>
        protected static readonly byte[] BytesOfBoth = Encoding.ASCII.GetBytes("age-encryption.org/v1\nstored as plain as well");

        /// <summary>How long a swap may take, as one waits for what plugins do with the contents and the clock stands still.</summary>
        private static readonly TimeSpan SwapPatience = TimeSpan.FromSeconds(60);

        private ServiceProvider _provider = null!;
        private AsyncServiceScope _scope;
        private readonly TestMetadataPlugins _plugins = new TestMetadataPlugins();
        private readonly List<AsyncServiceScope> _earlierScopes = new List<AsyncServiceScope>();

        protected TestRoot TestRoot { get; private set; } = null!;
        protected TestClock Clock { get; } = new TestClock(TestInstants.Now);
        protected IServiceProvider Services => _scope.ServiceProvider;

        protected IShelfService ShelfService => Services.GetRequiredService<IShelfService>();
        protected IUploadService UploadService => Services.GetRequiredService<IUploadService>();
        protected IFileService FileService => Services.GetRequiredService<IFileService>();
        protected IDirectoryService DirectoryService => Services.GetRequiredService<IDirectoryService>();
        protected ITrashService TrashService => Services.GetRequiredService<ITrashService>();
        protected ObjectStore ObjectStore => Services.GetRequiredService<ObjectStore>();
        protected StagingArea StagingArea => Services.GetRequiredService<StagingArea>();
        protected StorageOptions Options => Services.GetRequiredService<StorageOptions>();
        protected IChangeLogRepository ChangeLog => Services.GetRequiredService<IChangeLogRepository>();
        protected IShelfGrantService GrantService => Services.GetRequiredService<IShelfGrantService>();
        protected IShelfKeyService KeyService => Services.GetRequiredService<IShelfKeyService>();
        protected IShelfAccessResolver AccessResolver => Services.GetRequiredService<IShelfAccessResolver>();

        public virtual Task InitializeAsync()
        {
            TestRoot = TestRoot.Create();

            ServiceCollection services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            Program.AddDomainServices(services, ServerSettings.Open(TestRoot.Root) with { PublicAddress = new PublicAddress(AppBaseUrl) });
            _plugins.AddTo(services);
            ConfigureServices(services);

            // Not ValidateOnBuild: the auth registrations need routing, which only the web host adds.
            // The in-process HTTP tests build the real pipeline in Development, which validates it.
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _scope = _provider.CreateAsyncScope();
            return Task.CompletedTask;
        }

        public virtual async Task DisposeAsync()
        {
            await _scope.DisposeAsync();
            foreach (AsyncServiceScope earlier in _earlierScopes)
            {
                await earlier.DisposeAsync();
            }

            await _provider.DisposeAsync();
            await TestRoot.DisposeAsync();
        }

        /// <summary>Installs <paramref name="plugins"/> in place of those there were, with <see cref="Services"/> made anew, as after a restart.</summary>
        protected void InstallPlugins(params IReadOnlyList<TestMetadataPlugin> plugins)
        {
            _plugins.All = plugins;
            _earlierScopes.Add(_scope);
            _scope = _provider.CreateAsyncScope();
        }

        /// <summary>Replaces registrations for one test class; runs after the server's own.</summary>
        protected virtual void ConfigureServices(IServiceCollection services)
        {
        }

        /// <summary>A user with the given roles, made through the real service.</summary>
        protected Task<User> CreateUserAsync(string email, params string[] roles)
        {
            return Services.GetRequiredService<IUserService>().CreateUserAsync(email, "Test user", "password-for-tests", roles);
        }

        protected Task<Shelf> CreateShelfAsync(string name = "test", long? quotaBytes = null)
        {
            return ShelfService.CreateAsync(name, quotaBytes);
        }

        /// <summary>Stores <paramref name="text"/> as UTF-8 through a single PUT.</summary>
        protected Task<ShelfFile> StoreTextAsync(Shelf shelf, string path, string text, bool overwrite = false)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return UploadService.StoreAsync(shelf, Single(path, bytes.Length, "text/plain", overwrite), Uploader, new MemoryStream(bytes), CancellationToken.None);
        }

        /// <summary>Stores <paramref name="contents"/> typed <paramref name="contentType"/> through a single PUT, sent as encrypted or not.</summary>
        protected Task<ShelfFile> StoreAsync(Shelf shelf, string path, byte[] contents, string contentType, bool encrypted = false)
        {
            StartUploadRequest request = new StartUploadRequest { Path = path, SizeBytes = contents.Length, ContentType = contentType, Encrypted = encrypted };
            return UploadService.StoreAsync(shelf, request, Uploader, new MemoryStream(contents), CancellationToken.None);
        }

        /// <summary>Stores a photo of <paramref name="camera"/>'s, read by the server and by a plugin.</summary>
        protected async Task<ShelfFile> StoreReadPhotoAsync(Shelf shelf, string camera)
        {
            ShelfFile photo = await StoreAsync(shelf, "/photo.jpg", TestMedia.Photo(make: camera), "image/jpeg");
            await Services.ReadMetadataAsync();
            Assert.Equal(camera, (await Services.GetRequiredService<IObjectMetadataRepository>().GetBySha256Async(photo.Sha256))?.Metadata.Camera);
            Assert.True(await Services.RecordContentDocumentAsync(photo.Sha256, $"{{\"camera\":\"{camera}\"}}"));
            return photo;
        }

        /// <summary>Stores <see cref="BytesOfBoth"/> as a plain file and as an encrypted one.</summary>
        protected async Task<(ShelfFile Plain, ShelfFile Encrypted)> StorePlainAndEncryptedAsync(Shelf shelf)
        {
            return (await StoreAsync(shelf, "/plain.txt", BytesOfBoth, "text/plain"), await StoreAsync(shelf, "/secret.txt", BytesOfBoth, "text/plain", encrypted: true));
        }

        /// <summary>Swaps a plain file's contents for <see cref="AgeBytes"/>, in a scope of its own as a request has, so something the test runs meanwhile may do it.</summary>
        protected async Task EncryptInPlaceAsync(Shelf shelf, ShelfFile file)
        {
            StartContentSwapRequest request = new StartContentSwapRequest { FileId = file.Id, Sha256 = file.Sha256, SizeBytes = AgeBytes.Length, Encrypted = true };
            using IServiceScope scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IUploadService>().SwapAsync(shelf, request, Uploader, new MemoryStream(AgeBytes), CancellationToken.None).WaitAsync(SwapPatience);
        }

        /// <summary>Swaps an encrypted file's contents for <paramref name="text"/>, decrypting it in place.</summary>
        protected async Task DecryptInPlaceAsync(Shelf shelf, Guid fileId, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            ShelfFile file = await FileService.GetFileAsync(shelf, EntryReference.ForId(fileId));
            StartContentSwapRequest request = new StartContentSwapRequest { FileId = fileId, Sha256 = file.Sha256, SizeBytes = bytes.Length, Encrypted = false };
            using IServiceScope scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IUploadService>().SwapAsync(shelf, request, Uploader, new MemoryStream(bytes), CancellationToken.None).WaitAsync(SwapPatience);
        }

        /// <summary>Compacts the change log as the job does at the clock's time.</summary>
        protected async Task CompactChangeLogAsync()
        {
            await using IDbTransactionSession session = await Services.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync();
            await ChangeLog.CompactAsync(Clock.GetUtcNow(), session);
            await session.CommitAsync();
        }

        /// <summary>What a single PUT of <paramref name="sizeBytes"/> to <paramref name="path"/> asks for.</summary>
        protected static StartUploadRequest Single(string path, long sizeBytes, string? contentType = null, bool overwrite = false)
        {
            return new StartUploadRequest { Path = path, SizeBytes = sizeBytes, ContentType = contentType, Overwrite = overwrite };
        }

        protected async Task<string> ReadObjectTextAsync(string sha256)
        {
            await using FileStream stream = ObjectStore.OpenRead(sha256);
            using StreamReader reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        protected static string Sha256Of(string text)
        {
            return TestHashes.Sha256Of(text);
        }
    }
}
