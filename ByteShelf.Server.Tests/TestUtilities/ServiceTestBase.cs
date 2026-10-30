using System.Security.Cryptography;
using System.Text;
using ByteShelf.Server.ChangeLog;
using ByteShelf.Server.Email;
using ByteShelf.Server.Jobs;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Storage;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Users;
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

        private ServiceProvider _provider = null!;
        private AsyncServiceScope _scope;

        protected TestRoot TestRoot { get; private set; } = null!;
        protected FakeTimeProvider Clock { get; } = new FakeTimeProvider(TestInstants.Now);
        protected IServiceProvider Services => _scope.ServiceProvider;

        protected IShelfService ShelfService => Services.GetRequiredService<IShelfService>();
        protected IUploadService UploadService => Services.GetRequiredService<IUploadService>();
        protected IFileService FileService => Services.GetRequiredService<IFileService>();
        protected ITrashService TrashService => Services.GetRequiredService<ITrashService>();
        protected ObjectStore ObjectStore => Services.GetRequiredService<ObjectStore>();
        protected StagingArea StagingArea => Services.GetRequiredService<StagingArea>();
        protected StorageOptions Options => Services.GetRequiredService<StorageOptions>();

        public virtual Task InitializeAsync()
        {
            TestRoot = TestRoot.Create();

            ServiceCollection services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            services.AddDatabaseServices(TestRoot.Root);
            services.AddJobServices();
            services.AddChangeLogServices();
            services.AddStorageServices();
            services.AddEmailServices();
            services.AddUserServices(TestRoot.Root, AppBaseUrl);
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

        protected Task<Shelf> CreateShelfAsync(string name = "test", long? quotaBytes = null)
        {
            return ShelfService.CreateAsync(name, quotaBytes);
        }

        /// <summary>Stores <paramref name="text"/> as UTF-8 through a single PUT.</summary>
        protected Task<ShelfFile> StoreTextAsync(Shelf shelf, string path, string text, bool overwrite = false)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return UploadService.StoreAsync(shelf, path, "text/plain", bytes.Length, overwrite, new MemoryStream(bytes), CancellationToken.None);
        }

        protected async Task<string> ReadObjectTextAsync(string sha256)
        {
            await using FileStream stream = ObjectStore.OpenRead(sha256);
            using StreamReader reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        protected static string Sha256Of(string text)
        {
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }
    }
}
