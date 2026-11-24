namespace ByteShelf.Plugins
{
    /// <summary>
    /// The entry point of a plugin's server assembly, which holds exactly one public class implementing
    /// it, with a public constructor without parameters. The server makes one at startup, and runs its
    /// registration; so do <c>--check-plugin</c> and each child process the plugin's reader or maker runs in, which
    /// start nothing that is registered and have none of the server's settings. So neither the constructor nor the
    /// registration may have effects of their own.
    /// </summary>
    public interface IByteShelfPlugin
    {
        /// <summary>
        /// Registers what the plugin adds. It counts only if this returns and the plugin's controllers can
        /// be built, and a plugin that throws is not loaded. Controllers need no registering: every one in
        /// the plugin's assembly answers under <c>/api/plugins/{id}/</c>.
        /// </summary>
        void Configure(IPluginBuilder builder);
    }
}
