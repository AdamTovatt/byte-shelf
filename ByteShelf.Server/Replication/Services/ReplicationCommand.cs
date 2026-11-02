using ByteShelf.Server.Email;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Tools;
using ByteShelfCommon;

namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// <c>ByteShelf.Server --replication-status</c>, <c>--replication-code</c>, <c>--follow</c>,
    /// <c>--forget</c> and <c>--promote</c>: replication of the root at <c>BYTESHELF_ROOT</c> from the
    /// server's own machine, as its admins have it in the API, with the server running or stopped.
    /// They go through <see cref="ReplicationPairing"/>, by the API's rules. Following the primary is
    /// left to the server: one that runs starts within its poll after <c>--follow</c>, and stops applying
    /// the primary's changes after <c>--promote</c>, since a step only commits while the pairing it
    /// read is still there. Answers the process's exit code, one of <see cref="RootCommand"/>'s.
    /// </summary>
    public static class ReplicationCommand
    {
        /// <summary>The flags that name a replication command; the server runs this instead of starting when its arguments hold one.</summary>
        public static readonly IReadOnlyList<string> Flags = ["--replication-status", "--replication-code", "--follow", "--forget", "--promote"];

        private const string UsageText = """
            Usage, with BYTESHELF_ROOT set to the server's root:
              ByteShelf.Server --replication-status
              ByteShelf.Server --replication-code      (APP_BASE_URL set to this server's public URL, which the code names)
              ByteShelf.Server --follow <code> --name "<this server's name on the primary>"
              ByteShelf.Server --forget <replica id or name>
              ByteShelf.Server --promote
            """;

        /// <param name="args">The server's arguments.</param>
        /// <param name="rootPath"><c>BYTESHELF_ROOT</c>.</param>
        /// <param name="appBaseUrl"><c>APP_BASE_URL</c>, which a pairing code names; needed by <c>--replication-code</c> only.</param>
        /// <param name="loggerFactory">Where what it did, or why not, is logged.</param>
        public static Task<int> RunAsync(IReadOnlyList<string> args, string? rootPath, string? appBaseUrl, ILoggerFactory loggerFactory)
        {
            return RunAsync(args, rootPath, appBaseUrl, loggerFactory, configureServices: null);
        }

        /// <param name="configureServices">Runs once the command's services are registered: tests route its calls to a primary in memory.</param>
        internal static async Task<int> RunAsync(IReadOnlyList<string> args, string? rootPath, string? appBaseUrl, ILoggerFactory loggerFactory, Action<IServiceCollection>? configureServices)
        {
            ILogger logger = loggerFactory.CreateLogger(typeof(ReplicationCommand));
            ReplicationChange? change = Parse(args);
            if (change == null)
            {
                logger.LogError("{Usage}", UsageText);
                return RootCommand.Usage;
            }

            PublicAddress address = new PublicAddress(appBaseUrl);
            if (change is ReplicationChange.MakeCode && address.Configured == null)
            {
                logger.LogError("Set APP_BASE_URL to this server's public URL: the pairing code names it, for the replica to reach.");
                return RootCommand.Usage;
            }

            return await RootCommand.RunAsync(rootPath, logger, async root =>
            {
                await using ServiceProvider services = BuildServices(root, address, loggerFactory, configureServices);
                await using AsyncServiceScope scope = services.CreateAsyncScope();
                return await change.ApplyAsync(scope.ServiceProvider);
            });
        }

        /// <summary>
        /// The command <paramref name="args"/> ask for: exactly one of <see cref="Flags"/>, with its value
        /// where it takes one, <c>--name</c> with <c>--follow</c> only, and nothing else; null otherwise.
        /// </summary>
        internal static ReplicationChange? Parse(IReadOnlyList<string> args)
        {
            CommandArguments arguments = new CommandArguments(args);
            bool status = arguments.Flag("--replication-status");
            bool code = arguments.Flag("--replication-code");
            string? follow = arguments.Value("--follow");
            string? name = arguments.Value("--name");
            string? forget = arguments.Value("--forget");
            bool promote = arguments.Flag("--promote");

            ReplicationChange? change = (status, code, follow, forget, promote) switch
            {
                (true, false, null, null, false) when name == null => new ReplicationChange.ShowStatus(),
                (false, true, null, null, false) when name == null => new ReplicationChange.MakeCode(),
                (false, false, string pairingCode, null, false) when name != null => new ReplicationChange.Follow(pairingCode, name),
                (false, false, null, string replica, false) when name == null => new ReplicationChange.Forget(replica),
                (false, false, null, null, true) when name == null => new ReplicationChange.Promote(),
                _ => null,
            };
            return arguments.AllTaken ? change : null;
        }

        /// <summary>The services the API's replication goes through, on <paramref name="root"/>, without the web host.</summary>
        private static ServiceProvider BuildServices(RootFolder root, PublicAddress address, ILoggerFactory loggerFactory, Action<IServiceCollection>? configureServices)
        {
            ServiceCollection services = new ServiceCollection();
            services.AddSingleton(loggerFactory);
            services.AddLogging();

            // No email leaves a replication command.
            Program.AddDomainServices(services, root, address, EmailOptions.None);
            services.AddReplicationPairing(address.Absolute.AbsoluteUri);
            configureServices?.Invoke(services);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        /// <summary>One replication command, which says what it did.</summary>
        internal abstract record ReplicationChange
        {
            private ReplicationChange()
            {
            }

            public abstract Task<string> ApplyAsync(IServiceProvider services);

            internal sealed record ShowStatus : ReplicationChange
            {
                public override async Task<string> ApplyAsync(IServiceProvider services)
                {
                    return TextFormat.Replication(await services.GetRequiredService<ReplicationPairing>().GetStatusAsync());
                }
            }

            internal sealed record MakeCode : ReplicationChange
            {
                public override async Task<string> ApplyAsync(IServiceProvider services)
                {
                    // As the API refuses it on a replica, which cannot have replicas of its own.
                    await services.GetRequiredService<ReplicationState>().RequireWritesAsync();
                    return TextFormat.PairingCode(await services.GetRequiredService<ReplicationPairing>().CreateCodeAsync());
                }
            }

            internal sealed record Follow(string Code, string Name) : ReplicationChange
            {
                public override async Task<string> ApplyAsync(IServiceProvider services)
                {
                    ReplicationStatus status = await services.GetRequiredService<ReplicationPairing>().PairAsync(
                        new PairWithPrimaryRequest { Code = Code, Name = Name },
                        CancellationToken.None);
                    return TextFormat.Followed(status);
                }
            }

            internal sealed record Forget(string Replica) : ReplicationChange
            {
                public override async Task<string> ApplyAsync(IServiceProvider services)
                {
                    ReplicationPairing pairing = services.GetRequiredService<ReplicationPairing>();
                    ReplicaLookup lookup = Replicas.Find((await pairing.GetStatusAsync()).Replicas, Replica, "--replication-status");
                    ReplicaInfo replica = lookup.Found
                        ?? throw (lookup.Ambiguous ? new ConflictException(lookup.Refusal!) : new NotFoundException(lookup.Refusal!));
                    await pairing.RemoveReplicaAsync(replica.Id);
                    return TextFormat.ReplicaRemoved(replica);
                }
            }

            internal sealed record Promote : ReplicationChange
            {
                public override async Task<string> ApplyAsync(IServiceProvider services)
                {
                    bool primaryTold = await services.GetRequiredService<ReplicationPairing>().PromoteAsync(CancellationToken.None);
                    return TextFormat.Promoted(new PromoteResponse { PrimaryTold = primaryTold });
                }
            }
        }
    }
}
