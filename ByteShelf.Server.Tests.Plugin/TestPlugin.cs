using System.Globalization;
using ByteShelf.Plugins;
using ByteShelfCommon;
using EasyReasy.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Plugin
{
    /// <summary>
    /// Registers <see cref="PluginGreeting"/> and, for every line of <see cref="ReadersFileName"/> in its folder,
    /// <see cref="WordsReader"/> of the version and for the media types it names, or <see cref="NeedingReader"/>
    /// if there is a <see cref="NeedingReaderFileName"/> (TestPlugins.AddReadingServerPlugin), then throws if its folder holds <see cref="FailingFileName"/>
    /// (TestPlugins.AddFailingServerPlugin), or if <see cref="NeededFileName"/> in it names a service that
    /// was not registered before it (TestPlugins.AddServerPluginNeeding).
    /// </summary>
    public sealed class TestPlugin : IByteShelfPlugin
    {
        private const string FailingFileName = "fail-registration";

        private const string NeededFileName = "needed-service";

        private const string ReadersFileName = "readers";

        private const string NeedingReaderFileName = "needing-reader";

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

            string readers = Path.Combine(folder, ReadersFileName);
            foreach (string line in File.Exists(readers) ? File.ReadAllLines(readers) : [])
            {
                string[] words = line.Split(' ');
                int version = int.Parse(words[0], CultureInfo.InvariantCulture);
                if (File.Exists(Path.Combine(folder, NeedingReaderFileName)))
                {
                    builder.AddReader<NeedingReader>(version, words[1..]);
                }
                else
                {
                    builder.AddReader<WordsReader>(version, words[1..]);
                }
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
