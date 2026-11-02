using ByteShelf.Server.Email;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Users.Exceptions;
using ByteShelf.Server.Users.Models;
using ByteShelfCommon;
using Microsoft.Data.Sqlite;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// <c>ByteShelf.Server --create-user</c>, <c>--reset-password</c>, <c>--set-admin</c> and
    /// <c>--unset-admin</c>: changes users of the root at <c>BYTESHELF_ROOT</c> from the server's own
    /// machine, with the server running or stopped, for when no admin can sign in. They go through the
    /// services the API uses, so passwords are hashed and checked by the same rules, the last admin is
    /// kept, and the change log records each change for replicas. A replica refuses them, as its API
    /// refuses writes. Answers the process's exit code.
    /// </summary>
    public static class UserCommand
    {
        public const int Done = 0;

        /// <summary>Nothing was changed: no such user, a taken email, the last admin, a refused password, a replica, a damaged root.</summary>
        public const int Failed = 1;

        /// <summary>Nothing was tried: the arguments or <c>BYTESHELF_ROOT</c> are missing or wrong.</summary>
        public const int Usage = 2;

        /// <summary>The flags that name a user command; the server runs this instead of starting when its arguments hold one.</summary>
        public static readonly IReadOnlyList<string> Flags = ["--create-user", "--reset-password", "--set-admin", "--unset-admin"];

        /// <summary>How to run <c>--create-user</c>, for the usage text and for the server's log while it has no users.</summary>
        public const string CreateUserUsage = "ByteShelf.Server --create-user <email> --name \"<display name>\" [--admin]";

        private const string UsageText = $"""
            Usage, with BYTESHELF_ROOT set to the server's root:
              {CreateUserUsage}
              ByteShelf.Server --reset-password <email>
              ByteShelf.Server --set-admin <email>
              ByteShelf.Server --unset-admin <email>
            A new password is read from stdin when it is redirected, else typed twice at the terminal.
            """;

        public static async Task<int> RunAsync(IReadOnlyList<string> args, string? rootPath, IPasswordReader passwords, ILoggerFactory loggerFactory)
        {
            ILogger logger = loggerFactory.CreateLogger(typeof(UserCommand));
            UserChange? change = Parse(args);
            if (change == null)
            {
                logger.LogError("{Usage}", UsageText);
                return Usage;
            }

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                logger.LogError("Set BYTESHELF_ROOT to the server's root.");
                return Usage;
            }

            try
            {
                RootFolder root = RootFolder.OpenExisting(rootPath);
                try
                {
                    return await ApplyAsync(change, root, passwords, loggerFactory);
                }
                finally
                {
                    ReleaseDatabase(root);
                }
            }
            catch (Exception exception) when (exception is ConflictException or NotFoundException or ArgumentException or FileNotFoundException)
            {
                // What the person running it can act on, as the API would answer it.
                logger.LogError("{Message}", exception.Message);
                return Failed;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed: {Message}", exception.Message);
                return Failed;
            }
        }

        private static async Task<int> ApplyAsync(UserChange change, RootFolder root, IPasswordReader passwords, ILoggerFactory loggerFactory)
        {
            ILogger logger = loggerFactory.CreateLogger(typeof(UserCommand));

            // Not migrated here: the server may be running on this database, and an executable newer than
            // the server would upgrade it under it.
            if (!DatabaseMigrator.IsUpToDate(root.ConnectionString))
            {
                logger.LogError("The database is from an older version of the server: start this version on it, or run ByteShelf.Server --migrate-only, first.");
                return Failed;
            }

            await using ServiceProvider services = BuildServices(root, loggerFactory);
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<ReplicationState>().AllowsWritesAsync())
            {
                logger.LogError("This server is a replica, which takes its users from its primary; run this on the primary.");
                return Failed;
            }

            logger.LogInformation("{Done}", await change.ApplyAsync(scope.ServiceProvider, passwords));
            return Done;
        }

        /// <summary>
        /// Closes the pooled connections to the root's database, which outlive the service provider. With
        /// the server stopped this process is then the last to close the database, and SQLite removes its
        /// <c>-wal</c> and <c>-shm</c> files; left open until the process exits, they would stay, owned by
        /// whoever ran this, and a server running as another user could not open its database. (Refusing
        /// anyone but the root's owner would need the folder's owner, which .NET does not expose.)
        /// </summary>
        private static void ReleaseDatabase(RootFolder root)
        {
            using SqliteConnection connection = new SqliteConnection(root.ConnectionString);
            SqliteConnection.ClearPool(connection);
        }

        /// <summary>
        /// The change <paramref name="args"/> ask for: exactly one of <see cref="Flags"/> with its email,
        /// <c>--name</c> (and <c>--admin</c>, if wanted) with <c>--create-user</c> only, and nothing else;
        /// null when they ask for anything else.
        /// </summary>
        internal static UserChange? Parse(IReadOnlyList<string> args)
        {
            List<string> left = new List<string>(args);
            bool wellFormed = true;

            string? ValueOf(string flag)
            {
                int at = left.IndexOf(flag);
                if (at < 0)
                {
                    return null;
                }

                if (at + 1 >= left.Count || left[at + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    wellFormed = false;
                    left.RemoveAt(at);
                    return null;
                }

                string value = left[at + 1];
                left.RemoveRange(at, 2);
                return value;
            }

            string? create = ValueOf("--create-user");
            string? reset = ValueOf("--reset-password");
            string? setAdmin = ValueOf("--set-admin");
            string? unsetAdmin = ValueOf("--unset-admin");
            string? name = ValueOf("--name");
            bool admin = left.Remove("--admin");

            UserChange? change = (create, reset, setAdmin, unsetAdmin) switch
            {
                (string email, null, null, null) when name != null => new UserChange.Create(email, name, admin),
                (null, string email, null, null) when name == null && !admin => new UserChange.ResetPassword(email),
                (null, null, string email, null) when name == null && !admin => new UserChange.SetAdmin(email, Admin: true),
                (null, null, null, string email) when name == null && !admin => new UserChange.SetAdmin(email, Admin: false),
                _ => null,
            };
            return wellFormed && left.Count == 0 ? change : null;
        }

        /// <summary>The services the API changes users through, on <paramref name="root"/>, without the web host.</summary>
        internal static ServiceProvider BuildServices(RootFolder root, ILoggerFactory loggerFactory)
        {
            ServiceCollection services = new ServiceCollection();
            services.AddSingleton(loggerFactory);
            services.AddLogging();

            // No link or email leaves a user command: a reset link is never made here.
            Program.AddDomainServices(services, root, new PublicAddress(null), EmailOptions.None);
            services.AddReplicationState();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        /// <summary>One change to the users, which says what it did.</summary>
        internal abstract record UserChange
        {
            private UserChange()
            {
            }

            public abstract Task<string> ApplyAsync(IServiceProvider services, IPasswordReader passwords);

            /// <exception cref="NotFoundException">No user has <paramref name="email"/>.</exception>
            private protected static async Task<User> UserWithEmailAsync(IServiceProvider services, string email)
            {
                return await services.GetRequiredService<IUserService>().GetUserByEmailAsync(email) ?? throw NoUserWith(email);
            }

            private protected static NotFoundException NoUserWith(string email)
            {
                return new NotFoundException($"No user has the email '{email}'.");
            }

            internal sealed record Create(string Email, string DisplayName, bool Admin) : UserChange
            {
                public override async Task<string> ApplyAsync(IServiceProvider services, IPasswordReader passwords)
                {
                    // Checked before the password is typed, so a mistake is not reported only after it.
                    // CreateUserAsync checks them again, as it must for the API.
                    UserFields.ValidateEmail(Email);
                    UserFields.NormalizeDisplayName(DisplayName);
                    IUserService users = services.GetRequiredService<IUserService>();
                    if (await users.GetUserByEmailAsync(Email) != null)
                    {
                        throw new DuplicateEmailException(Email);
                    }

                    string password = passwords.ReadNewPassword(Email);
                    await users.CreateUserAsync(Email, DisplayName, password, Admin ? [UserRoles.Admin] : null);
                    return Admin ? $"Created the admin {Email}." : $"Created the user {Email}.";
                }
            }

            internal sealed record ResetPassword(string Email) : UserChange
            {
                public override async Task<string> ApplyAsync(IServiceProvider services, IPasswordReader passwords)
                {
                    // Looked up first, so a mistyped email is not found only after the password is typed.
                    User user = await UserWithEmailAsync(services, Email);
                    string password = passwords.ReadNewPassword(Email);
                    if (!await services.GetRequiredService<IPasswordResetService>().SetPasswordAsync(user.Id, password))
                    {
                        throw NoUserWith(Email);
                    }

                    return $"Set a new password for {Email}; every session of theirs has ended.";
                }
            }

            internal sealed record SetAdmin(string Email, bool Admin) : UserChange
            {
                public override async Task<string> ApplyAsync(IServiceProvider services, IPasswordReader passwords)
                {
                    User user = await UserWithEmailAsync(services, Email);
                    if (user.Roles.Contains(UserRoles.Admin) == Admin)
                    {
                        return Admin ? $"{Email} is already an admin." : $"{Email} is not an admin.";
                    }

                    IEnumerable<string> roles = Admin
                        ? user.Roles.Append(UserRoles.Admin)
                        : user.Roles.Where(role => role != UserRoles.Admin);
                    await services.GetRequiredService<IUserService>().UpdateRolesAsync(user.Id, roles);
                    return Admin ? $"{Email} is an admin." : $"{Email} is no longer an admin.";
                }
            }
        }
    }
}
