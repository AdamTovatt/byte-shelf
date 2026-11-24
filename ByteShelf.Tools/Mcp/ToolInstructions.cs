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
            a depth if you give one), search_files finds files under a directory by text in the name,
            description, or a title or camera the contents carry, media type (image/* for a family),
            size, times, SHA-256, whether it has a description, or what the contents say: when a photo
            or video was taken, whether it has a GPS position, how long it plays, a PDF's pages. It
            also finds files by the fields the server's plugins declare.
            find_duplicates groups the files among them that have exactly the same contents, most
            repeated bytes first, to tidy away copies (encrypted and empty files never match). get_info
            answers one file's or directory's record, a file's with what the server read from its
            contents (when and where a photo or video was taken, the camera, a video's length, a PDF's
            title and pages) and the fields plugins declare. A long answer comes a page at a time and ends with
            "More follow: call again with after = <cursor>"; pass that cursor as after for the next
            page.

            Reading: download_file answers a UTF-8 file of up to 256 KiB as text, and a
            {ByteShelfMcpTools.InlineImageKinds} image of up to 1 MiB as an image you can see. On the
            ByteShelf server's MCP, any other file stored as an image (image/*) of up to 32 MiB comes as
            a JPEG preview at most 1568 pixels on its long side, saying its full size and where to get
            it whole; one it cannot preview (HEIC, say) comes only with why and where to get it.
            Through the byteshelf CLI's MCP, save a larger image with localPath and look at it there.
            share_file makes a link anyone can open without signing in, for 24 hours or the time asked
            for (at most 7 days); use it to hand a person a file, or to fetch anything else. A link
            answers with its id: list_links shows the links that have not run out, and revoke_link
            ends one sooner. To read a file once, make its link for a few minutes and revoke it once
            read. A share link stops working while whoever made it can no longer read the shelf.

            Storing: upload_file stores text, or a binary file's contents as base64, each up to 256 KiB,
            creating missing directories. It never replaces a file unless overwrite is true, and then
            the old one goes to the trash. create_directory makes an empty directory.
            create_upload_link makes a link that stores one file at a path when anyone who has it PUTs
            the contents to it, for 15 minutes or the time asked for (at most 24 hours), with the curl
            command that does it: use it for a file too large to pass here, or one a person or another
            program will send.

            Describing: describe files with set_description (or upload_file's description) so they can
            be found later. search_files with hasDescription false finds the files that have no
            description yet. A good description says what the file is, who or what it is about, where
            and when, and the words someone would search for that its name lacks. Read a file before
            describing it; describe one you cannot read (encrypted, too large) from its name, its path
            and what get_info says about it, and say no more than those tell.

            Changing: move renames or moves files and directories and never overwrites. move and
            set_description take a list, so one call tidies many entries (a list of one for a single
            entry): each item is applied on its own, in order, and the answer says for each whether it
            was done or why it was refused; a refused one undoes nothing. delete moves a file or
            directory to the trash; list_trash shows what can come back, restore brings a deletion back, and
            empty_trash deletes the trash for good. download_deleted reads what a deletion took without
            restoring it, as download_file and download_directory read; a replica answers it too, which
            gives back a file purged on its primary while it is still in the replica's trash.

            Encrypted: an entry marked (encrypted) is encrypted with a password, which never goes through
            tool calls. download_file and download_deleted refuse an encrypted file, download_directory
            and download_deleted leave them out of a directory, no
            link is made to one, and nothing can be stored in an encrypted directory through these
            tools. Names, sizes and descriptions stay visible, so search still finds them; a person opens
            them in the web UI or with the byteshelf CLI.

            A refusal ends with its code in parentheses, such as (not_found), (conflict) or
            (permission_denied).
            """;
    }
}
