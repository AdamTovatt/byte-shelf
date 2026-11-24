using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Plugins.Controllers;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Shared.StaticFiles;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;

namespace ByteShelf.Server.Plugins
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>What confines plugins' controllers to their addresses; <see cref="LoadPlugins"/> loads the plugins.</summary>
        public static IServiceCollection AddPluginServices(this IServiceCollection services)
        {
            services.AddSingleton<IActionDescriptorProvider, PluginRouteProvider>();
            return services;
        }

        /// <summary>
        /// Loads the plugins under <paramref name="pluginsPath"/>, none when it is null, adding their
        /// registrations to the server's own, which must all be there, and has files' metadata know them.
        /// </summary>
        public static void LoadPlugins(this IMvcBuilder mvc, string? pluginsPath, ILogger logger)
        {
            PluginCatalog catalog = PluginLoader.LoadAll(PluginDiscovery.Find(pluginsPath, logger), mvc.Services, mvc.PartManager, logger);
            mvc.Services.AddSingleton<IPluginCatalog>(catalog);
            mvc.Services.Replace(ServiceDescriptor.Singleton<IMetadataPlugins, LoadedMetadataPlugins>());
        }

        /// <summary>
        /// Serves each plugin's <c>ui</c> folder, and nothing else of it, at an address that changes with
        /// what is in it. To anyone: a browser loads modules and styles without the session's token.
        /// </summary>
        public static void UsePluginAssets(this WebApplication app)
        {
            foreach (PluginUi ui in app.Services.GetRequiredService<IPluginCatalog>().Loaded.Select(plugin => plugin.Ui).OfType<PluginUi>())
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(ui.Folder),
                    RequestPath = ui.RequestPath,
                    OnPrepareResponse = context => StaticFileCachePolicy.ApplyImmutable(context.Context.Response),

                    // What a plugin ships for itself to read, such as fonts' tables, under any extension.
                    ServeUnknownFileTypes = true,
                });
            }
        }
    }
}
