using System.Security.Cryptography;
using System.Text;
using ByteShelf.Server.Access.Services;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Email;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

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

        private ServiceProvider _provider = null!;
        private AsyncServiceScope _scope;

        protected TestRoot TestRoot { get; private set; } = null!;
        protected FakeTimeProvider Clock { get; } = new FakeTimeProvider(TestInstants.Now);
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
            Program.AddDomainServices(services, TestRoot.Root, new PublicAddress(AppBaseUrl), EmailOptions.None);
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
            await _provider.DisposeAsync();
            await TestRoot.DisposeAsync();
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
