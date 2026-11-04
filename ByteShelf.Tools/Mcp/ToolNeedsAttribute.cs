namespace ByteShelf.Tools.Mcp
{
    /// <summary>What a tool asks of its caller's access to the shelf it names.</summary>
    internal enum ToolNeed
    {
        /// <summary>Reading: listing, searching, downloading, sharing by a link.</summary>
        Read,

        /// <summary>Adding: new files and directories, what an add-only key may do.</summary>
        Add,

        /// <summary>Changing or removing what is there.</summary>
        Write,

        /// <summary>Work on the server as a whole, such as creating shelves; never offered over a shelf key.</summary>
        Admin,
    }

    /// <summary>
    /// What a tool needs, so a server that serves the tools to a shelf key offers only those the key's
    /// permission allows, and only reads on a replica. The server's own endpoints still check every call;
    /// this keeps an agent from being offered tools that could only fail.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ToolNeedsAttribute : Attribute
    {
        public ToolNeedsAttribute(ToolNeed need)
        {
            Need = need;
        }

        public ToolNeed Need { get; }

        /// <summary>Whether the tool works only on files local to the agent, which a remote server cannot reach.</summary>
        public bool LocalFilesOnly { get; init; }
    }
}
