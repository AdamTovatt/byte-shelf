using System.Reflection;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>What went wrong in a plugin's code.</summary>
    internal static class PluginFailure
    {
        /// <summary>
        /// The exception that says why, of those wrapped around it on the way: that what was invoked threw,
        /// that a type's initializer did, and, once for every type, that types could not be loaded.
        /// </summary>
        public static Exception CauseOf(Exception exception)
        {
            return exception switch
            {
                TargetInvocationException or TypeInitializationException when exception.InnerException is { } inner => CauseOf(inner),
                ReflectionTypeLoadException { LoaderExceptions: [{ } first, ..] } => CauseOf(first),
                _ => exception,
            };
        }
    }
}
