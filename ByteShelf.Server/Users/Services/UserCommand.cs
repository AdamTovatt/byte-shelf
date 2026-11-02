using ByteShelf.Server.Email;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Users.Exceptions;
using ByteShelf.Server.Users.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// <c>ByteShelf.Server --create-user</c>, <c>--reset-password</c>, <c>--set-admin</c> and
    /// <c>--unset-admin</c>: changes users of the root at <c>BYTESHELF_ROOT</c> from the server's own
    /// machine, with the server running or stopped, for when no admin can sign in. They go through the
    /// services the API uses, so passwords are hashed and checked by the same rules, the last admin is
    /// kept, and the change log records each change for replicas. A replica refuses them, as its API
    /// refuses writes. Answers the process's exit code, one of <see cref="RootCommand"/>'s: refused
    /// for no such user, a taken email, the last admin, a refused password or a replica.
    /// </summary>
    public static class UserCommand
    {
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
                return RootCommand.Usage;
            }

            return await RootCommand.RunAsync(rootPath, logger, async root =>
            {
                await using ServiceProvider services = BuildServices(root, loggerFactory);
                await using AsyncServiceScope scope = services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ReplicationState>().RequireWritesAsync();
                return await change.ApplyAsync(scope.ServiceProvider, passwords);
            });
        }

        /// <summary>
        /// The change <paramref name="args"/> ask for: exactly one of <see cref="Flags"/> with its email,
        /// <c>--name</c> (and <c>--admin</c>, if wanted) with <c>--create-user</c> only, and nothing else;
        /// null when they ask for anything else.
        /// </summary>
        internal static UserChange? Parse(IReadOnlyList<string> args)
        {
            CommandArguments arguments = new CommandArguments(args);
            string? create = arguments.Value("--create-user");
            string? reset = arguments.Value("--reset-password");
            string? setAdmin = arguments.Value("--set-admin");
            string? unsetAdmin = arguments.Value("--unset-admin");
            string? name = arguments.Value("--name");
            bool admin = arguments.Flag("--admin");

            UserChange? change = (create, reset, setAdmin, unsetAdmin) switch
            {
                (string email, null, null, null) when name != null => new UserChange.Create(email, name, admin),
                (null, string email, null, null) when name == null && !admin => new UserChange.ResetPassword(email),
                (null, null, string email, null) when name == null && !admin => new UserChange.SetAdmin(email, Admin: true),
                (null, null, null, string email) when name == null && !admin => new UserChange.SetAdmin(email, Admin: false),
                _ => null,
            };
            return arguments.AllTaken ? change : null;
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
