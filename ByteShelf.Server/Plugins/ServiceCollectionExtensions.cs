using ByteShelf.Server.Plugins.Controllers;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Shared.StaticFiles;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.Extensions.FileProviders;

namespace ByteShelf.Server.Plugins
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Loads the plugins under <paramref name="pluginsPath"/>, none when it is null. After every
        /// service of the server's own, which the plugins' registrations are added to.
        /// </summary>
        public static IServiceCollection AddPlugins(this IServiceCollection services, IMvcBuilder mvc, string? pluginsPath, ILogger logger)
        {
            services.AddSingleton<IActionDescriptorProvider, PluginRouteProvider>();
            PluginCatalog catalog = PluginLoader.LoadAll(PluginDiscovery.Find(pluginsPath, logger), services, mvc.PartManager, logger);
            services.AddSingleton<IPluginCatalog>(catalog);
            return services;
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
