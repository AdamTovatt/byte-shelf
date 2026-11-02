namespace ByteShelf.Tools.Mcp
{
    /// <summary>What both MCP servers tell an agent ByteShelf is and how its tools fit together, sent when it connects.</summary>
    internal static class ToolInstructions
    {
        public const string Text = $"""
            ByteShelf is a self-hosted file store. Files live on shelves: each shelf is its own tree of
            directories and files, with its own trash and quota. Tools take paths that start with /
            (the shelf's root is /) and a shelf to work on, which you can leave out when you reach
            only one shelf. list_shelves shows the shelves you can reach, and shelf_info one shelf's
            usage and quota.

            Looking around: list_directory shows one directory, list_tree everything under one (down to
            a depth if you give one), search_files finds files under a directory by text in the name or
            description, media type (image/* for a family), size, times or SHA-256. get_info answers
            one file's or directory's record. A long answer comes a page at a time and ends with
            "More follow: call again with after = <cursor>"; pass that cursor as after for the next
            page.

            Reading: download_file answers a UTF-8 file of up to 256 KiB as text, and a
            {ByteShelfMcpTools.InlineImageKinds} image of up to 1 MiB as an image you can see. On the
            ByteShelf server's MCP, any other file stored as an image (image/*) of up to 32 MiB comes as
            a JPEG preview at most 1568 pixels on its long side, saying its full size and where to get
            it whole; one it cannot preview (HEIC, say) comes only with why and where to get it.
            Through the byteshelf CLI's MCP, save a larger image with localPath and look at it there.
            share_file makes a link anyone can open without signing in, for 24 hours or the time asked
            for (at most 7 days); use it to hand a person a file, or to fetch anything else.

            Storing: upload_file stores text, or a binary file's contents as base64, each up to 256 KiB,
            creating missing directories. It never replaces a file unless overwrite is true, and then
            the old one goes to the trash. create_directory makes an empty directory. Describe a file
            with set_description (or upload_file's description) so it can be found later.
            create_upload_link makes a link that stores one file at a path when anyone who has it PUTs
            the contents to it, for 15 minutes or the time asked for (at most 24 hours), with the curl
            command that does it: use it for a file too large to pass here, or one a person or another
            program will send.

            Changing: move renames or moves a file or directory and never overwrites. delete moves it
            to the trash; list_trash shows what can come back, restore brings a deletion back, and
            empty_trash deletes the trash for good.

            Encrypted: an entry marked (encrypted) is encrypted with a password, which never goes through
            tool calls. download_file refuses an encrypted file, download_directory leaves them out, no
            link is made to one, and nothing can be stored in an encrypted directory through these
            tools. Names, sizes and descriptions stay visible, so search still finds them; a person opens
            them in the web UI or with the byteshelf CLI.

            A refusal ends with its code in parentheses, such as (not_found), (conflict) or
            (permission_denied).
            """;
    }
}
