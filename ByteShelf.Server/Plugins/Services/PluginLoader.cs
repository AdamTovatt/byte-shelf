using System.Reflection;
using ByteShelf.Plugins;
using ByteShelf.Server.Plugins.Models;
using Microsoft.AspNetCore.Mvc.ApplicationParts;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>
    /// Loads plugins. Loading one runs its code with the server's own trust; what goes wrong in loading it,
    /// in registering its services or in building its controllers is the plugin's failure alone.
    /// </summary>
    public static class PluginLoader
    {
        /// <summary>
        /// Loads the plugins of <paramref name="candidates"/>, in order of id, as <see cref="Load"/> does.
        /// One that cannot be loaded is logged and skipped.
        /// </summary>
        public static PluginCatalog LoadAll(IReadOnlyList<PluginCandidate> candidates, IServiceCollection services, ApplicationPartManager parts, ILogger logger)
        {
            List<LoadedPlugin> loaded = new List<LoadedPlugin>();
            List<SkippedPlugin> skipped = new List<SkippedPlugin>();

            foreach (PluginCandidate candidate in candidates.OrderBy(candidate => candidate.Id, StringComparer.Ordinal))
            {
                try
                {
                    PluginCandidate? other = candidate.Id == null ? null : candidates.FirstOrDefault(each => each != candidate && each.Id == candidate.Id);
                    if (other != null)
                    {
                        throw new PluginRefusedException($"The plugin in {other.Folder} has the id {candidate.Id} too.");
                    }

                    LoadedPlugin plugin = Load(candidate, services, parts);
                    loaded.Add(plugin);
                    logger.LogInformation("Loaded the plugin {Id} {Version} from {Folder}.", plugin.Manifest.Id, plugin.Manifest.Version, candidate.Folder);
                }
                catch (PluginRefusedException refusal)
                {
                    logger.LogError(refusal.InnerException, "Skipped the plugin in {Folder}: {Reason}", candidate.Folder, refusal.Message);
                    skipped.Add(candidate.SkippedFor(refusal.Message));
                }
            }

            return new PluginCatalog(loaded, skipped.OrderBy(plugin => plugin.Folder, StringComparer.Ordinal).ToList());
        }

        /// <summary>
        /// Loads one plugin, registering its services in <paramref name="services"/> and its controllers in
        /// <paramref name="parts"/>, which are the server's: both are left as they were when it fails. (Not
        /// undone is what its registration did to an object registered there, such as the names
        /// <c>AddHttpClient&lt;T&gt;</c> keeps.)
        /// </summary>
        /// <exception cref="PluginRefusedException">It cannot be loaded.</exception>
        public static LoadedPlugin Load(PluginCandidate candidate, IServiceCollection services, ApplicationPartManager parts)
        {
            ReadablePlugin plugin = candidate switch
            {
                ReadablePlugin readable => readable,
                UnreadablePlugin unreadable => throw new PluginRefusedException(unreadable.Skipped.Reason),
                _ => throw new InvalidOperationException($"No way to load a {candidate.GetType().Name}."),
            };

            PluginUi? ui = Attempt("Its ui folder could not be read", () => PluginUiReader.Read(plugin.Manifest.Id, plugin.Folder));
            if (plugin.Manifest.ServerAssemblyPath != null)
            {
                ApplicationPart[] partsBefore = parts.ApplicationParts.ToArray();
                try
                {
                    Replace(services, Register(plugin.Manifest.Id, plugin.Manifest.ServerAssemblyPath, services, parts));
                }
                catch
                {
                    Replace(parts.ApplicationParts, partsBefore);
                    throw;
                }
            }

            return new LoadedPlugin(plugin.Manifest, ui);
        }

        /// <summary>A copy of <paramref name="services"/> with the plugin's added, its controllers in <paramref name="parts"/> tried.</summary>
        private static ServiceCollection Register(string pluginId, string assemblyPath, IServiceCollection services, ApplicationPartManager parts)
        {
            (Assembly assembly, Type entry) = Attempt("Its server assembly could not be loaded", () => LoadAssembly(pluginId, assemblyPath));

            // A copy rather than an empty collection: a registration made with TryAdd, as the framework's
            // own AddLogging and AddHttpClient are, then sees what the server has and adds no second one.
            ServiceCollection registered = new ServiceCollection();
            Replace(registered, services);

            Attempt("Registering its services failed", () =>
            {
                IByteShelfPlugin plugin = (IByteShelfPlugin)Activator.CreateInstance(entry)!;
                plugin.ConfigureServices(registered);
                return plugin;
            });

            parts.ApplicationParts.Add(new AssemblyPart(assembly));
            Attempt("Its controllers could not be built", () =>
            {
                PluginEndpoints.Try(registered);
                return parts;
            });

            return registered;
        }

        private static (Assembly Assembly, Type Entry) LoadAssembly(string pluginId, string path)
        {
            Assembly assembly = new PluginLoadContext(pluginId, path).LoadFromAssemblyPath(path);

            // Every type, not only the public ones: the server looks through them all for controllers.
            List<Type> entries = assembly.GetTypes()
                .Where(type => type.IsVisible && type.IsClass && !type.IsAbstract && type.IsAssignableTo(typeof(IByteShelfPlugin)))
                .ToList();

            return entries.Count == 1
                ? (assembly, entries[0])
                : throw new PluginRefusedException($"Its server assembly must hold one public class implementing {nameof(IByteShelfPlugin)}; it holds {entries.Count}.");
        }

        private static void Replace<T>(ICollection<T> items, IEnumerable<T> replacement)
        {
            items.Clear();
            foreach (T item in replacement)
            {
                items.Add(item);
            }
        }

        /// <summary>Runs <paramref name="step"/>, whose failure, whatever it is, is the plugin's.</summary>
        private static T Attempt<T>(string failure, Func<T> step)
        {
            try
            {
                return step();
            }
            catch (PluginRefusedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Exception cause = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
                throw new PluginRefusedException($"{failure}: {cause.Message}", cause);
            }
        }
    }
}
