using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ByteShelf.Cli.Connection;
using ByteShelf.Tools;
using ByteShelfClient;
using ByteShelfClient.Encryption;
using ByteShelfCommon;
using ByteShelfCommon.Serialization;

namespace ByteShelf.Cli.Commands
{
    /// <summary>Runs one <c>byteshelf</c> command line and answers its exit code.</summary>
    internal sealed class CliApp
    {
        public const int Succeeded = 0;
        public const int Failed = 1;
        public const int Misused = 2;
        public const int Interrupted = 130;

        private const int UsageColumn = 40;

        private readonly IClientSource _clients;
        private readonly Terminal _terminal;
        private readonly int _encryptionWorkFactor;
        private readonly IReadOnlyList<Command> _commands;
        private bool _json;

        /// <summary>The CLI as it runs: it encrypts at age's default work factor.</summary>
        public CliApp(IClientSource clients, Terminal terminal)
            : this(clients, terminal, AgeEncryption.DefaultWorkFactor)
        {
        }

        /// <param name="encryptionWorkFactor">The scrypt work factor it encrypts with; only tests give a lower one, to run fast.</param>
        internal CliApp(IClientSource clients, Terminal terminal, int encryptionWorkFactor)
        {
            _clients = clients;
            _terminal = terminal;
            _encryptionWorkFactor = encryptionWorkFactor;
            _commands = new[]
            {
                new Command(Section.SigningIn, "login", "<server-url> --email <email> | --key", "sign in as a user, or with a shelf key; the password or key is read from the terminal or stdin", LoginAsync),
                new Command(Section.SigningIn, "logout", string.Empty, "end the session and forget the login", LogoutAsync),
                new Command(Section.SigningIn, "whoami", string.Empty, "the server and who commands act as", WhoAmIAsync),

                new Command(Section.Shelves, "shelves", string.Empty, "list the shelves you can reach", ListShelvesAsync),
                new Command(Section.Shelves, "shelf info", "<shelf>", "a shelf's usage and quota", ShelfInfoAsync),
                new Command(Section.Shelves, "shelf create", "<shelf> [--quota <bytes>]", "create a shelf", CreateShelfAsync),
                new Command(Section.Shelves, "shelf update", "<shelf> [--name <new-name>] [--quota <bytes> | --no-quota]", "rename a shelf or change its quota", UpdateShelfAsync),
                new Command(Section.Shelves, "shelf delete", "<shelf>", "delete an empty shelf, trash included", DeleteShelfAsync),

                new Command(Section.Files, "ls", "<shelf>[:<path>]", "what a directory holds", ListAsync),
                new Command(Section.Files, "stat", "<shelf>:<path>", "a file's record, or a directory's", StatAsync),
                new Command(Section.Files, "tree", "<shelf>[:<path>] [--depth <n>] [--limit <n>] [--after <cursor>]", "everything under a directory, by path, at any depth or down to --depth levels", TreeAsync),
                new Command(Section.Files, "put", "<local-file>|<local-folder>|- <shelf>:<path> [--overwrite] [--type <content-type>] [--description <text>] [--encrypt] [--password-file <file>]", "store a file, - for stdin, or a whole folder; a path ending in / keeps the local name; encrypted with the folder's password under an encrypted folder, or with a new one given --encrypt", PutAsync),
                new Command(Section.Files, "get", "<shelf>:<path> [<local-file>|<local-folder>|-] [--overwrite] [--decrypt] [--password-file <file>]", "save a file, - for stdout, or a whole directory; an encrypted file is saved as its .age file unless --decrypt or --password-file decrypts it", GetAsync),
                new Command(Section.Files, "encrypt", "<shelf>:<path> [--password-file <file>]", "encrypt a file, or a folder and every file under it, with a password that never leaves this machine; the unencrypted contents are deleted at once, and a lost password cannot be recovered", EncryptAsync),
                new Command(Section.Files, "decrypt", "<shelf>:<path> [--password-file <file>]", "remove a file's or a folder's encryption", DecryptAsync),
                new Command(Section.Files, "describe", "<shelf>:<path> <text> | --clear", "set a file's description, which find matches too", DescribeAsync),
                new Command(Section.Files, "share", "<shelf>:<path> [--expires <30m|12h|7d>]", string.Create(CultureInfo.InvariantCulture, $"a link to a file that works without signing in; {DownloadLink.Lifetime.Default.TotalHours} hours by default, at most {DownloadLink.Lifetime.Max.TotalDays} days"), ShareAsync),
                new Command(
                    Section.Files,
                    "find",
                    "<shelf>[:<path>] [<text>] [--type <content-type>|'<type>/*'] [--min-size <bytes>] [--max-size <bytes>] [--created-from <time>] [--created-before <time>] [--updated-from <time>] [--updated-before <time>] [--sha256 <hex>] [--limit <n>] [--after <cursor>]",
                    "files under a directory whose name or description holds <text>, ignoring case, and that meet every option; times are ISO 8601, local without an offset",
                    FindAsync),
                new Command(Section.Files, "mkdir", "<shelf>:<path>", "create a directory with any missing parents", MakeDirectoryAsync),
                new Command(Section.Files, "mv", "<shelf>:<path> <new-path>", "move or rename a file or a directory within its shelf", MoveAsync),
                new Command(Section.Files, "rm", "<shelf>:<path>", "move a file, or a directory with everything in it, to the trash", RemoveAsync),

                new Command(Section.Trash, "trash", "<shelf>", "what can be restored, with each deletion's id", TrashAsync),
                new Command(Section.Trash, "restore", "<shelf> <deletion-id>", "put back what a deletion took", RestoreAsync),
                new Command(Section.Trash, "empty-trash", "<shelf>", "delete a shelf's trash for good", EmptyTrashAsync),

                new Command(Section.Users, "passwd", string.Empty, "change your password; both are read from the terminal or stdin", ChangePasswordAsync),
                new Command(Section.Users, "users", string.Empty, "every user", ListUsersAsync),
                new Command(Section.Users, "user create", "<email> --name <display-name> [--admin]", "create a user; the password is read from the terminal or stdin", CreateUserAsync),
                new Command(Section.Users, "user admin", "<user>", "make a user an admin; <user> is an email or an id", (arguments, token) => SetAdminAsync(arguments, admin: true, token)),
                new Command(Section.Users, "user no-admin", "<user>", "make an admin a user again", (arguments, token) => SetAdminAsync(arguments, admin: false, token)),
                new Command(Section.Users, "user delete", "<user>", "delete a user", DeleteUserAsync),
                new Command(Section.Users, "grants", "<shelf>", "who has been given access to a shelf", ListGrantsAsync),
                new Command(Section.Users, "grant", $"<shelf> <user> {PermissionNames.AccessWords}", "give a user access to a shelf", GrantAsync),
                new Command(Section.Users, "revoke", "<shelf> <user>", "take a user's access to a shelf away", RevokeAsync),
                new Command(Section.Users, "keys", "<shelf>", "a shelf's keys", ListKeysAsync),
                new Command(Section.Users, "key create", $"<shelf> <name> {PermissionNames.KeyWords}", "create a key; its secret is shown once", CreateKeyAsync),
                new Command(Section.Users, "key delete", "<shelf> <key-id>", "delete a key", DeleteKeyAsync),
            };
        }

        private enum Section
        {
            SigningIn,
            Shelves,
            Files,
            Trash,
            Users,
        }

        /// <summary>Every command, by section, with the options every command takes.</summary>
        public string Help
        {
            get
            {
                StringBuilder help = new StringBuilder("byteshelf: a ByteShelf server from the terminal. Places on a shelf are written shelf:/path.");
                foreach (IGrouping<Section, Command> section in _commands.GroupBy(command => command.Section))
                {
                    help.AppendLine().AppendLine().Append(SectionTitle(section.Key));
                    foreach (Command command in section)
                    {
                        string usage = $"  {command.Name} {command.Arguments}".TrimEnd();
                        help.AppendLine().Append(usage.PadRight(UsageColumn));
                        help.Append(usage.Length < UsageColumn ? " " : Environment.NewLine + new string(' ', UsageColumn + 1));
                        help.Append(command.Summary);
                    }
                }

                return help.AppendLine().AppendLine()
                    .AppendLine("Options: --json prints results as JSON, and nothing for a command without one. -- ends the options, for a path starting with --.")
                    .Append("byteshelf --mcp runs an MCP server on stdin and stdout.")
                    .ToString();
            }
        }

        public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
        {
            try
            {
                List<string> words = args.Where(word => word != "--json").ToList();
                _json = words.Count != args.Count;
                if (words.Count == 0 || words[0] is "help" or "--help" or "-h")
                {
                    _terminal.Output.WriteLine(Help);
                    return Succeeded;
                }

                (Command command, int nameWords) = Find(words);
                await command.RunAsync(ArgumentList.Parse(words.Skip(nameWords).ToList()), cancellationToken);
                return Succeeded;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Interrupted;
            }
            catch (Exception exception) when (Failure.From(exception) is Failure failure)
            {
                _terminal.Error.WriteLine(failure.Message);
                return failure.IsMisuse ? Misused : Failed;
            }
        }

        private static string SectionTitle(Section section)
        {
            return section switch
            {
                Section.SigningIn => "Signing in (kept in the config file; BYTESHELF_URL and BYTESHELF_KEY override it):",
                Section.Shelves => "Shelves:",
                Section.Files => "Files and directories:",
                Section.Trash => "Trash:",
                _ => "Users, access and keys (admins, except passwd):",
            };
        }

        /// <summary>The command the first one or two words name, and how many words its name takes.</summary>
        /// <exception cref="UsageException">They name none.</exception>
        private (Command Command, int NameWords) Find(List<string> words)
        {
            Command? command = words.Count > 1 ? _commands.FirstOrDefault(candidate => candidate.Name == $"{words[0]} {words[1]}") : null;
            if (command != null)
            {
                return (command, 2);
            }

            command = _commands.FirstOrDefault(candidate => candidate.Name == words[0]);
            if (command != null)
            {
                return (command, 1);
            }

            string[] subcommands = _commands.Where(candidate => candidate.Name.StartsWith(words[0] + " ", StringComparison.Ordinal)).Select(candidate => candidate.Name).ToArray();
            throw new UsageException(subcommands.Length > 0
                ? $"'{words[0]}' needs a subcommand: {string.Join(", ", subcommands)}."
                : $"Unknown command '{words[0]}'. Run 'byteshelf help' for the commands.");
        }

        private async Task LoginAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string server = arguments.Required(0, "server-url");
            string? email = arguments.Value("--email");
            bool useKey = arguments.Flag("--key");
            arguments.EnsureAllUsed();
            if ((email == null) == !useKey)
            {
                throw new UsageException("Sign in with --email <email> or with --key, one of them.");
            }

            if (useKey)
            {
                await _clients.UseKeyAsync(server, _terminal.ReadSecret("Shelf key: ", cancellationToken), cancellationToken);
            }
            else
            {
                await _clients.SignInAsync(server, email!, _terminal.ReadSecret("Password: ", cancellationToken), cancellationToken);
            }

            _terminal.Output.WriteLine($"Signed in to {_clients.Describe()}.");
        }

        private async Task LogoutAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureAllUsed();
            await _clients.SignOutAsync(cancellationToken);
            Done("Signed out.");
        }

        private async Task WhoAmIAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureAllUsed();
            string description = _clients.Describe() ?? throw new UsageException("Not signed in. Run 'byteshelf login'.");
            IByteShelfClient client = await _clients.OpenAsync(cancellationToken);
            try
            {
                Print(await client.GetCurrentUserAsync(cancellationToken), ByteShelfJsonContext.Default.UserInfo, user => $"{description}{Environment.NewLine}{TextFormat.User(user)}");
            }
            catch (ByteShelfException exception) when (exception.Code == ErrorCodes.Unauthenticated)
            {
                // A key is not a user, so the users endpoint refuses it; the shelves it reaches say
                // what it is. A session that has ended is refused here too, and throws on.
                Print(await client.GetShelvesAsync(cancellationToken), ByteShelfJsonContext.Default.IReadOnlyListShelfInfo, shelves => $"{description}{Environment.NewLine}{TextFormat.Shelves(shelves)}");
            }
        }

        private async Task ListShelvesAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureAllUsed();
            Print(await (await _clients.OpenAsync(cancellationToken)).GetShelvesAsync(cancellationToken), ByteShelfJsonContext.Default.IReadOnlyListShelfInfo, TextFormat.Shelves);
        }

        private async Task ShelfInfoAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string shelf = arguments.Required(0, "shelf");
            arguments.EnsureAllUsed();
            Print(await (await ShelfAsync(shelf, cancellationToken)).GetInfoAsync(cancellationToken), ByteShelfJsonContext.Default.ShelfInfo, TextFormat.Shelf);
        }

        private async Task CreateShelfAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string name = arguments.Required(0, "shelf");
            long? quota = arguments.Bytes("--quota");
            arguments.EnsureAllUsed();
            Print(await (await _clients.OpenAsync(cancellationToken)).CreateShelfAsync(name, quota, cancellationToken), ByteShelfJsonContext.Default.ShelfInfo, TextFormat.Shelf);
        }

        private async Task UpdateShelfAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string shelfName = arguments.Required(0, "shelf");
            string? name = arguments.Value("--name");
            long? quota = arguments.Bytes("--quota");
            bool noQuota = arguments.Flag("--no-quota");
            arguments.EnsureAllUsed();
            if (noQuota && quota != null)
            {
                throw new UsageException("Give --quota or --no-quota, not both.");
            }

            // The update replaces both settings, so whatever is not given keeps its current value.
            IShelfClient shelf = await ShelfAsync(shelfName, cancellationToken);
            ShelfInfo current = await shelf.GetInfoAsync(cancellationToken);
            ShelfInfo updated = await shelf.UpdateAsync(name ?? current.Name, noQuota ? null : quota ?? current.QuotaBytes, cancellationToken);
            Print(updated, ByteShelfJsonContext.Default.ShelfInfo, TextFormat.Shelf);
        }

        private async Task DeleteShelfAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string shelf = arguments.Required(0, "shelf");
            arguments.EnsureAllUsed();
            await (await ShelfAsync(shelf, cancellationToken)).DeleteAsync(cancellationToken);
            Done("Deleted the shelf.");
        }

        private async Task ListAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            ShelfLocation location = ShelfLocation.Parse(arguments.Required(0, "shelf:path"));
            arguments.EnsureAllUsed();
            DirectoryListing listing = await (await ShelfAsync(location.Shelf, cancellationToken)).ListAsync(location.Path, cancellationToken);
            Print(listing, ByteShelfJsonContext.Default.DirectoryListing, TextFormat.Listing);
        }

        private async Task StatAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            ShelfLocation location = ShelfLocation.Parse(arguments.Required(0, "shelf:path"));
            arguments.EnsureAllUsed();
            PrintEntry(await Entries.GetAsync(await ShelfAsync(location.Shelf, cancellationToken), location.Path, cancellationToken), TextFormat.Entry);
        }

        private async Task PutAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string source = arguments.Required(0, "local-file");
            ShelfLocation location = ShelfLocation.Parse(arguments.Required(1, "shelf:path"));
            UploadOptions options = new UploadOptions
            {
                Overwrite = arguments.Flag("--overwrite"),
                ContentType = arguments.Value("--type"),
                Description = arguments.Value("--description"),
            };
            bool encrypt = arguments.Flag("--encrypt");
            Func<PasswordUse, string> password = PasswordFrom(arguments, source == "-", cancellationToken);
            arguments.EnsureAllUsed();
            IShelfClient shelf = await ShelfAsync(location.Shelf, cancellationToken);
            bool isFolder = source != "-" && Directory.Exists(source);
            string target = source == "-" ? location.Path : location.FilePathFor(Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));

            // Under an encrypted folder the files must be encrypted with its password; elsewhere only when asked.
            EncryptedFolder? folder = (await shelf.GetEncryptionAsync(target, cancellationToken)).EncryptedBy;
            Encryptor? encryptWith = folder != null
                ? new Encryptor(await Encrypting.FolderPasswordAsync(folder, password, cancellationToken), _encryptionWorkFactor)
                : encrypt ? new Encryptor(password(PasswordUse.New), _encryptionWorkFactor) : null;

            if (isFolder)
            {
                if (options.ContentType != null || options.Description != null)
                {
                    throw new UsageException("--type and --description are for one file, not a folder.");
                }

                FolderDone(await FolderTransfers.UploadAsync(shelf, source, location, options.Overwrite, cancellationToken, encryptWith), "Uploaded");
                return;
            }

            ShelfFile stored;
            if (source == "-")
            {
                await using Stream input = _terminal.OpenInput();
                stored = await Transfers.UploadStreamAsync(shelf, location, input, "Standard input", options, cancellationToken, encryptWith);
            }
            else
            {
                stored = await Transfers.UploadFileAsync(shelf, source, location, options, cancellationToken, encryptWith);
            }

            Print(stored, ByteShelfJsonContext.Default.ShelfFile, TextFormat.Stored);
        }

        private async Task GetAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            ShelfLocation location = ShelfLocation.Parse(arguments.Required(0, "shelf:path"));
            string? target = arguments.Optional(1);
            bool overwrite = arguments.Flag("--overwrite");
            // A password file alone asks to decrypt too.
            bool decryptAsked = arguments.Flag("--decrypt");
            bool passwordFileGiven = arguments.Value("--password-file") != null;
            Func<PasswordUse, string> password = PasswordFrom(arguments, readsInput: false, cancellationToken);
            arguments.EnsureAllUsed();
            bool decrypt = decryptAsked || passwordFileGiven;
            IShelfClient shelf = await ShelfAsync(location.Shelf, cancellationToken);

            Entry? entry = location.Path == ShelfPath.Root ? null : await Entries.GetAsync(shelf, location.Path, cancellationToken);
            if (entry?.File is not ShelfFile file)
            {
                if (target == "-")
                {
                    throw new UsageException($"'{location}' is a directory; save it to a local folder, not to stdout.");
                }

                EncryptedFiles encrypted = decrypt
                    ? EncryptedFiles.DecryptedWith(await Encrypting.ExistingPasswordAsync(shelf, location.Path, password, cancellationToken))
                    : EncryptedFiles.SavedAsTheyAre;
                FolderDone(await FolderTransfers.DownloadAsync(shelf, location, target, overwrite, cancellationToken, encrypted), "Saved");
                return;
            }

            // An encrypted file is saved as the age file it is stored as, unless asked to decrypt it.
            EncryptedFiles fileEncrypted = file.Encrypted && decrypt ? EncryptedFiles.DecryptedWith(password(PasswordUse.Existing)) : EncryptedFiles.SavedAsTheyAre;
            if (target == "-")
            {
                await using Stream output = _terminal.OpenOutput();
                await Transfers.WriteAsync(shelf, file, output, fileEncrypted, cancellationToken);
                return;
            }

            string saved = await Transfers.DownloadToFileAsync(shelf, file, target ?? ".", overwrite, fileEncrypted, cancellationToken);
            Done(fileEncrypted.KeepsEncrypted(file) ? TextFormat.SavedEncrypted(saved) : TextFormat.Saved(saved));
        }

        private async Task EncryptAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            ShelfLocation location = ShelfLocation.Parse(arguments.Required(0, "shelf:path"));
            Func<PasswordUse, string> password = PasswordFrom(arguments, readsInput: false, cancellationToken);
            arguments.EnsureAllUsed();

            FolderDone(await Encrypting.EncryptAsync(await ShelfAsync(location.Shelf, cancellationToken), location, password, _encryptionWorkFactor, cancellationToken), "Encrypted", "under");
        }

        private async Task DecryptAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            ShelfLocation location = ShelfLocation.Parse(arguments.Required(0, "shelf:path"));
            Func<PasswordUse, string> password = PasswordFrom(arguments, readsInput: false, cancellationToken);
            arguments.EnsureAllUsed();

            FolderDone(await Encrypting.DecryptAsync(await ShelfAsync(location.Shelf, cancellationToken), location, password, cancellationToken), "Decrypted", "under");
        }

        /// <summary>
        /// Where a command's password comes from, asked only once it is needed: the first line of
        /// <c>--password-file</c>, or the terminal without echo (or the next line of piped input), a new
        /// one typed twice. Never an argument, which would land in the shell's history.
        /// </summary>
        /// <param name="readsInput">Whether the command's standard input is its contents, which leaves the file the only way.</param>
        private Func<PasswordUse, string> PasswordFrom(ArgumentList arguments, bool readsInput, CancellationToken cancellationToken)
        {
            string? file = arguments.Value("--password-file");
            return use =>
            {
                if (file != null)
                {
                    string? line = File.Exists(file) ? File.ReadLines(file).FirstOrDefault() : throw new UsageException($"There is no password file {Path.GetFullPath(file)}.");
                    return string.IsNullOrEmpty(line) ? throw new UsageException($"The password file {Path.GetFullPath(file)} is empty.") : line;
                }

                if (readsInput)
                {
                    throw new UsageException("Standard input carries the contents, so give the password with --password-file.");
                }

                string password = _terminal.ReadSecret("Password: ", cancellationToken);
                if (use == PasswordUse.New && _terminal.ReadSecret("Password again: ", cancellationToken) != password)
                {
                    throw new UsageException("The two passwords differ.");
                }

                return password;
            };
        }

        private async Task DescribeAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            ShelfLocation location = ShelfLocation.Parse(arguments.Required(0, "shelf:path"));
            string? text = arguments.Optional(1);
            bool clear = arguments.Flag("--clear");
            arguments.EnsureAllUsed();
            if ((text == null) == !clear)
            {
                throw new UsageException("Give the description's <text>, or --clear to remove it, one of them.");
            }

            ShelfFile described = await (await ShelfAsync(location.Shelf, cancellationToken)).SetDescriptionAsync(location.Path, clear ? null : text, cancellationToken);
            Print(described, ByteShelfJsonContext.Default.ShelfFile, TextFormat.File);
        }

        private async Task ShareAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            ShelfLocation location = ShelfLocation.Parse(arguments.Required(0, "shelf:path"));
            TimeSpan? lifetime = LinkLifetimes.ForDownload(arguments.Value("--expires"));
            arguments.EnsureAllUsed();

            DownloadLink link = await (await ShelfAsync(location.Shelf, cancellationToken)).CreateDownloadLinkAsync(location.Path, lifetime, cancellationToken);
            Print(link, ByteShelfJsonContext.Default.DownloadLink, TextFormat.Link);
        }

        private async Task FindAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            ShelfLocation location = ShelfLocation.Parse(arguments.Required(0, "shelf:path"));
            FileSearch search = new FileSearch
            {
                Under = location.Path,
                Text = arguments.Optional(1),
                ContentType = arguments.Value("--type"),
                MinSizeBytes = arguments.Bytes("--min-size"),
                MaxSizeBytes = arguments.Bytes("--max-size"),
                CreatedFrom = arguments.Time("--created-from"),
                CreatedBefore = arguments.Time("--created-before"),
                UpdatedFrom = arguments.Time("--updated-from"),
                UpdatedBefore = arguments.Time("--updated-before"),
                Sha256 = arguments.Value("--sha256"),
                Limit = arguments.Count("--limit"),
                After = arguments.Value("--after"),
            };
            arguments.EnsureAllUsed();

            FileSearchResults found = await (await ShelfAsync(location.Shelf, cancellationToken)).SearchAsync(search, cancellationToken);
            Print(found, ByteShelfJsonContext.Default.FileSearchResults, results => TextFormat.SearchResults(results, MoreAfter));
        }

        private async Task TreeAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            ShelfLocation location = ShelfLocation.Parse(arguments.Required(0, "shelf:path"));
            TreeQuery query = new TreeQuery
            {
                Under = location.Path,
                Depth = arguments.Count("--depth"),
                Limit = arguments.Count("--limit"),
                After = arguments.Value("--after"),
            };
            arguments.EnsureAllUsed();

            DirectoryTree tree = await (await ShelfAsync(location.Shelf, cancellationToken)).ListTreeAsync(query, cancellationToken);
            Print(tree, ByteShelfJsonContext.Default.DirectoryTree, page => TextFormat.Tree(page, MoreAfter));
        }

        /// <summary>How to ask for the page after <paramref name="cursor"/>, quoted for a POSIX shell.</summary>
        private static string MoreAfter(string cursor)
        {
            return $"More: add --after '{cursor.Replace("'", "'\\''", StringComparison.Ordinal)}'";
        }

        /// <summary>Prints what a folder transfer did; one that did not reach everything fails, so a script sees it.</summary>
        /// <exception cref="IOException">Something was left out, or the transfer stopped early.</exception>
        private void FolderDone(FolderTransferResult result, string verb, string where = "to")
        {
            Done(TextFormat.FolderTransfer(result, verb, where));
            if (!result.IsComplete)
            {
                throw new IOException(TextFormat.FolderTransferFailure(result));
            }
        }

        private async Task MakeDirectoryAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            ShelfLocation location = ShelfLocation.Parse(arguments.Required(0, "shelf:path"));
            arguments.EnsureAllUsed();
            ShelfDirectory created = await (await ShelfAsync(location.Shelf, cancellationToken)).CreateDirectoryAsync(location.Path, cancellationToken);
            Print(created, ByteShelfJsonContext.Default.ShelfDirectory, TextFormat.Directory);
        }

        private async Task MoveAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            ShelfLocation location = ShelfLocation.Parse(arguments.Required(0, "shelf:path"));
            string toPath = arguments.Required(1, "new-path");
            arguments.EnsureAllUsed();
            Entry moved = await Entries.MoveAsync(await ShelfAsync(location.Shelf, cancellationToken), location.Path, toPath, cancellationToken);
            PrintEntry(moved, TextFormat.Moved);
        }

        private async Task RemoveAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            ShelfLocation location = ShelfLocation.Parse(arguments.Required(0, "shelf:path"));
            arguments.EnsureAllUsed();
            TrashEntry deleted = await Entries.DeleteAsync(await ShelfAsync(location.Shelf, cancellationToken), location.Path, cancellationToken);
            Print(deleted, ByteShelfJsonContext.Default.TrashEntry, TextFormat.Trashed);
        }

        private async Task TrashAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string shelf = arguments.Required(0, "shelf");
            arguments.EnsureAllUsed();
            Print(await (await ShelfAsync(shelf, cancellationToken)).GetTrashAsync(cancellationToken), ByteShelfJsonContext.Default.IReadOnlyListTrashEntry, TextFormat.Trash);
        }

        private async Task RestoreAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string shelf = arguments.Required(0, "shelf");
            Guid deletionId = Id(arguments.Required(1, "deletion-id"), $"a deletion id; 'byteshelf trash {shelf}' lists them");
            arguments.EnsureAllUsed();
            await (await ShelfAsync(shelf, cancellationToken)).RestoreAsync(deletionId, cancellationToken);
            Done(TextFormat.Restored);
        }

        private async Task EmptyTrashAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string shelf = arguments.Required(0, "shelf");
            arguments.EnsureAllUsed();
            await (await ShelfAsync(shelf, cancellationToken)).EmptyTrashAsync(cancellationToken);
            Done(TextFormat.EmptiedTrash);
        }

        private async Task ChangePasswordAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureAllUsed();
            IByteShelfClient client = await _clients.OpenAsync(cancellationToken);
            string current = _terminal.ReadSecret("Current password: ", cancellationToken);
            string replacement = _terminal.ReadSecret("New password: ", cancellationToken);
            await client.ChangePasswordAsync(current, replacement, cancellationToken);
            Done("Changed your password.");
        }

        private async Task ListUsersAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            arguments.EnsureAllUsed();
            Print(await (await _clients.OpenAsync(cancellationToken)).GetUsersAsync(cancellationToken), ByteShelfJsonContext.Default.IReadOnlyListUserInfo, TextFormat.Users);
        }

        private async Task CreateUserAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string email = arguments.Required(0, "email");
            string displayName = arguments.Value("--name") ?? throw new UsageException("Give the user's --name.");
            bool admin = arguments.Flag("--admin");
            arguments.EnsureAllUsed();
            IByteShelfClient client = await _clients.OpenAsync(cancellationToken);
            CreateUserRequest request = new CreateUserRequest
            {
                Email = email,
                DisplayName = displayName,
                Password = _terminal.ReadSecret($"Password for {email}: ", cancellationToken),
                Roles = admin ? new[] { UserRoles.Admin } : Array.Empty<string>(),
            };

            Print(await client.CreateUserAsync(request, cancellationToken), ByteShelfJsonContext.Default.UserInfo, TextFormat.User);
        }

        private async Task SetAdminAsync(ArgumentList arguments, bool admin, CancellationToken cancellationToken)
        {
            string user = arguments.Required(0, "user");
            arguments.EnsureAllUsed();
            IByteShelfClient client = await _clients.OpenAsync(cancellationToken);
            UserInfo found = await FindUserAsync(client, user, cancellationToken);
            IReadOnlyList<string> roles = admin
                ? found.Roles.Append(UserRoles.Admin).ToList()
                : found.Roles.Where(role => role != UserRoles.Admin).ToList();
            await client.SetUserRolesAsync(found.Id, roles, cancellationToken);
            Done(admin ? $"{found.Email} is an admin." : $"{found.Email} is no longer an admin.");
        }

        private async Task DeleteUserAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string user = arguments.Required(0, "user");
            arguments.EnsureAllUsed();
            IByteShelfClient client = await _clients.OpenAsync(cancellationToken);
            UserInfo found = await FindUserAsync(client, user, cancellationToken);
            await client.DeleteUserAsync(found.Id, cancellationToken);
            Done($"Deleted {found.Email}.");
        }

        private async Task ListGrantsAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string shelf = arguments.Required(0, "shelf");
            arguments.EnsureAllUsed();
            Print(await (await ShelfAsync(shelf, cancellationToken)).GetGrantsAsync(cancellationToken), ByteShelfJsonContext.Default.IReadOnlyListShelfAccessGrant, TextFormat.Grants);
        }

        private async Task GrantAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string shelf = arguments.Required(0, "shelf");
            string user = arguments.Required(1, "user");
            ShelfAccessPermission permission = PermissionNames.ParseAccess(arguments.Required(2, "access"));
            arguments.EnsureAllUsed();
            UserInfo found = await FindUserAsync(await _clients.OpenAsync(cancellationToken), user, cancellationToken);
            ShelfAccessGrant granted = await (await ShelfAsync(shelf, cancellationToken)).SetGrantAsync(found.Id, permission, cancellationToken);
            Print(granted, ByteShelfJsonContext.Default.ShelfAccessGrant, TextFormat.Grant);
        }

        private async Task RevokeAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string shelf = arguments.Required(0, "shelf");
            string user = arguments.Required(1, "user");
            arguments.EnsureAllUsed();
            UserInfo found = await FindUserAsync(await _clients.OpenAsync(cancellationToken), user, cancellationToken);
            await (await ShelfAsync(shelf, cancellationToken)).RemoveGrantAsync(found.Id, cancellationToken);
            Done($"{found.Email} no longer has access to {shelf}.");
        }

        private async Task ListKeysAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string shelf = arguments.Required(0, "shelf");
            arguments.EnsureAllUsed();
            Print(await (await ShelfAsync(shelf, cancellationToken)).GetKeysAsync(cancellationToken), ByteShelfJsonContext.Default.IReadOnlyListShelfKeyInfo, TextFormat.Keys);
        }

        private async Task CreateKeyAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string shelf = arguments.Required(0, "shelf");
            string name = arguments.Required(1, "name");
            ShelfKeyPermission permission = PermissionNames.ParseKey(arguments.Required(2, "access"));
            arguments.EnsureAllUsed();
            Print(await (await ShelfAsync(shelf, cancellationToken)).CreateKeyAsync(name, permission, cancellationToken), ByteShelfJsonContext.Default.CreatedShelfKey, TextFormat.CreatedKey);
        }

        private async Task DeleteKeyAsync(ArgumentList arguments, CancellationToken cancellationToken)
        {
            string shelf = arguments.Required(0, "shelf");
            Guid keyId = Id(arguments.Required(1, "key-id"), $"a key id; 'byteshelf keys {shelf}' lists them");
            arguments.EnsureAllUsed();
            await (await ShelfAsync(shelf, cancellationToken)).DeleteKeyAsync(keyId, cancellationToken);
            Done("Deleted the key.");
        }

        /// <exception cref="UsageException"><paramref name="text"/> is not an id.</exception>
        private static Guid Id(string text, string what)
        {
            return Guid.TryParse(text, out Guid id) ? id : throw new UsageException($"'{text}' is not {what}.");
        }

        /// <summary>The user named by email or id.</summary>
        private static async Task<UserInfo> FindUserAsync(IByteShelfClient client, string user, CancellationToken cancellationToken)
        {
            IReadOnlyList<UserInfo> users = await client.GetUsersAsync(cancellationToken);
            UserInfo? found = Guid.TryParse(user, out Guid id)
                ? users.FirstOrDefault(candidate => candidate.Id == id)
                : users.FirstOrDefault(candidate => string.Equals(candidate.Email, user, StringComparison.OrdinalIgnoreCase));
            return found ?? throw new ByteShelfException($"There is no user '{user}'.", ErrorCodes.NotFound);
        }

        private async Task<IShelfClient> ShelfAsync(string name, CancellationToken cancellationToken)
        {
            return (await _clients.OpenAsync(cancellationToken)).Shelf(name);
        }

        private void Print<T>(T value, JsonTypeInfo<T> type, Func<T, string> text)
        {
            _terminal.Output.WriteLine(_json ? JsonSerializer.Serialize(value, type) : text(value));
        }

        private void PrintEntry(Entry entry, Func<Entry, string> text)
        {
            if (_json)
            {
                _terminal.Output.WriteLine(entry.File != null
                    ? JsonSerializer.Serialize(entry.File, ByteShelfJsonContext.Default.ShelfFile)
                    : JsonSerializer.Serialize(entry.Directory!, ByteShelfJsonContext.Default.ShelfDirectory));
            }
            else
            {
                _terminal.Output.WriteLine(text(entry));
            }
        }

        /// <summary>Reports an action without a result to print; with --json, prints nothing.</summary>
        private void Done(string message)
        {
            if (!_json)
            {
                _terminal.Output.WriteLine(message);
            }
        }

        private sealed record Command(Section Section, string Name, string Arguments, string Summary, Func<ArgumentList, CancellationToken, Task> RunAsync);
    }
}
