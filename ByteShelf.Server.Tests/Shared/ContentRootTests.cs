using ByteShelf.Server.Email;
using ByteShelf.Server.Tests.TestUtilities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace ByteShelf.Server.Tests.Shared
{
    /// <summary>Where the server finds its web UI and settings, whichever folder it is started from.</summary>
    [Collection(nameof(WorkingDirectoryCollection))]
    public sealed class ContentRootTests
    {
        [Fact]
        public async Task BuildApplication_StartedFromAnotherFolder_TakesTheExecutablesFolderAsItsContentRoot()
        {
            await using TestRoot root = TestRoot.Create();
            string elsewhere = Path.Combine(root.Root.Path, "elsewhere");
            Directory.CreateDirectory(elsewhere);
            string before = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(elsewhere);
            try
            {
                await using WebApplication app = Program.BuildApplication(
                    ["--environment", "Development"],
                    root.Root,
                    builder => builder.WebHost.UseTestServer(),
                    EmailOptions.None);

                Assert.Equal(
                    Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
                    Path.TrimEndingDirectorySeparator(app.Environment.ContentRootPath));
            }
            finally
            {
                Directory.SetCurrentDirectory(before);
            }
        }
    }
}
