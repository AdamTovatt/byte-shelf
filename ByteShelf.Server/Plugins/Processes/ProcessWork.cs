using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Services;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// A kind of work plugins do over contents in their process: the module it is for registers it as a
    /// <see cref="ProcessWork"/> service, and a run names it by its type (<see cref="IPluginRun{T}.Work"/>).
    /// </summary>
    internal abstract class ProcessWork
    {
        /// <summary>What a run request calls the work of <paramref name="work"/>, a <see cref="ProcessWork"/>.</summary>
        public static string NameOf(Type work)
        {
            return work.Name;
        }

        /// <summary>
        /// How the plugin that registered <paramref name="registered"/> does it. What it needs of the plugin is made now
        /// (<see cref="Ready"/>), so that one that cannot be made is not the contents' doing.
        /// </summary>
        public abstract PluginWork For(PluginRegistrations registered);

        /// <summary>Makes it, and answers it whenever it is asked for, or why it cannot be.</summary>
        /// <param name="what">What it is to the plugin, for the reason given.</param>
        /// <param name="make">Makes it; null when the plugin registered none.</param>
        protected static Func<T> Ready<T>(string what, Func<T>? make)
        {
            try
            {
                if (make == null)
                {
                    return () => throw new PluginUnavailableException($"it has no {what}");
                }

                T instance = make();
                return () => instance;
            }
            catch (Exception exception)
            {
                string reason = PluginFailure.CauseOf(exception).Message.TrimEnd('.');
                return () => throw new PluginUnavailableException($"its {what} could not be made: {reason}");
            }
        }
    }
}
