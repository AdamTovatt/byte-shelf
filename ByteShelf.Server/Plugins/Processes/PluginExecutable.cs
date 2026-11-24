namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>What a child process is started from: the server's own executable.</summary>
    /// <param name="FileName">The program.</param>
    /// <param name="Arguments">What comes before the child's own arguments.</param>
    public sealed record PluginExecutable(string FileName, IReadOnlyList<string> Arguments)
    {
        /// <summary>The executable this server runs from: its own, or <c>dotnet</c> with its assembly.</summary>
        public static PluginExecutable OfThisServer()
        {
            return Of(Environment.ProcessPath ?? throw new InvalidOperationException("The server's own executable is not known."), AppContext.BaseDirectory);
        }

        /// <summary>The executable a server runs from whose process is of the program <paramref name="process"/> and whose assemblies are in <paramref name="folder"/>.</summary>
        internal static PluginExecutable Of(string process, string folder)
        {
            return Path.GetFileNameWithoutExtension(process) == "dotnet"
                ? new PluginExecutable(process, [Path.Combine(folder, typeof(PluginExecutable).Assembly.GetName().Name + ".dll")])
                : new PluginExecutable(process, []);
        }
    }
}
