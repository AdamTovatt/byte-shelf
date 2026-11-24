using ByteShelf.Plugins;
using ByteShelfCommon;
using EasyReasy.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Plugin
{
    /// <summary>Registers <see cref="PluginGreeting"/>, then throws if its folder holds <see cref="FailingFileName"/> (TestPlugins.AddFailingServerPlugin).</summary>
    public sealed class TestPlugin : IByteShelfPlugin
    {
        private const string FailingFileName = "fail-registration";

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(new PluginGreeting(
                typeof(VariableName).Assembly.GetName().Version!.ToString(),
                typeof(ShelfPath).Assembly.Location));

            // As a plugin that calls out would; it adds to registrations the server has made already.
            services.AddHttpClient("test-plugin");

            // Its controllers again, as a plugin may; the server adds them itself.
            services.AddControllers().AddApplicationPart(typeof(TestPlugin).Assembly);

            if (File.Exists(Path.Combine(Path.GetDirectoryName(typeof(TestPlugin).Assembly.Location)!, FailingFileName)))
            {
                throw new InvalidOperationException("It was told to fail.");
            }
        }
    }
}
