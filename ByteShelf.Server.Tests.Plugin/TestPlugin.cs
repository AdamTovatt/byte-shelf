using ByteShelf.Plugins;
using ByteShelfCommon;
using EasyReasy.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Plugin
{
    /// <summary>
    /// Registers <see cref="PluginGreeting"/>, then throws if its folder holds <see cref="FailingFileName"/>
    /// (TestPlugins.AddFailingServerPlugin), or if <see cref="NeededFileName"/> in it names a service that
    /// was not registered before it (TestPlugins.AddServerPluginNeeding).
    /// </summary>
    public sealed class TestPlugin : IByteShelfPlugin
    {
        private const string FailingFileName = "fail-registration";

        private const string NeededFileName = "needed-service";

        public void Configure(IPluginBuilder builder)
        {
            IServiceCollection services = builder.Services;
            services.AddSingleton(new PluginGreeting(
                typeof(VariableName).Assembly.GetName().Version!.ToString(),
                typeof(ShelfPath).Assembly.Location));

            string folder = Path.GetDirectoryName(typeof(TestPlugin).Assembly.Location)!;
            string needed = Path.Combine(folder, NeededFileName);
            if (File.Exists(needed) && !services.Any(descriptor => descriptor.ServiceType.FullName == File.ReadAllText(needed)))
            {
                throw new InvalidOperationException($"There is no {File.ReadAllText(needed)}.");
            }

            // As a plugin that calls out would; it adds to registrations the server has made already.
            services.AddHttpClient("test-plugin");

            // Its controllers again, as a plugin may; the server adds them itself.
            services.AddControllers().AddApplicationPart(typeof(TestPlugin).Assembly);

            if (File.Exists(Path.Combine(folder, FailingFileName)))
            {
                throw new InvalidOperationException("It was told to fail.");
            }
        }
    }
}
