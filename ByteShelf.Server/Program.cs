using System.Net;
using ByteShelf.Server.Access;
using ByteShelf.Server.Backup.Services;
using ByteShelf.Server.ChangeLog;
using ByteShelf.Server.Email;
using ByteShelf.Server.Invalidation;
using ByteShelf.Server.Jobs;
using ByteShelf.Server.Mcp;
using ByteShelf.Server.Metadata;
using ByteShelf.Server.Plugins;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Json;
using ByteShelf.Server.Shared.Middleware;
using ByteShelf.Server.Shared.RateLimiting;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Shared.StaticFiles;
using ByteShelf.Server.Storage;
using ByteShelf.Server.Users;
using ByteShelf.Server.Users.Services;
using ByteShelf.Server.Version;
using ByteShelfCommon;
using EasyReasy.Auth;
using EasyReasy.EnvironmentVariables;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;

namespace ByteShelf.Server
{
    public class Program
    {
        /// <summary>
        /// The longest request line the server takes. Paths travel in the query string, and a search
        /// carries two (<c>under</c> and <c>after</c>) besides its text. Percent-encoded, a UTF-16 unit
        /// takes at most 9 bytes (3 bytes of UTF-8, each as %XX), so this fits the longest of each with
        /// room for the rest; Kestrel's own 8 KiB answers 414 to one long non-ASCII path.
        /// </summary>
        internal const int MaxRequestLineBytes = 9 * ((2 * ShelfPath.MaxPathLength) + FileDescription.MaxLength) + (8 * 1024);

        public static void Main(string[] args)
        {
            if (args.Contains(PluginCheckCommand.Flag))
            {
                Environment.ExitCode = PluginCheckCommand.Run(args, Console.Out, Console.Error);
                return;
            }

            if (args.Contains("--schema-only"))
            {
                RunSchemaOnlyMode(args);
                return;
            }

            if (args.Contains("--migrate-only"))
            {
                RunMigrateOnlyMode();
                return;
            }

            int backup = Array.IndexOf(args, "--backup");
            if (backup >= 0)
            {
                // Only BYTESHELF_ROOT is read: the root belongs to the server running on it.
                using ILoggerFactory loggerFactory = CreateStartupLoggerFactory();
                Environment.ExitCode = BackupCommand.Run(
                    EnvironmentVariables.ByteShelfRoot.GetValueOrDefault(),
                    args.ElementAtOrDefault(backup + 1),
                    loggerFactory.CreateLogger<Program>());
                return;
            }

            if (UserCommand.Flags.Any(args.Contains))
            {
                // Beside BYTESHELF_ROOT no setting is needed: no web host, no email.
                using ILoggerFactory loggerFactory = CreateStartupLoggerFactory();
                Environment.ExitCode = UserCommand.RunAsync(
                    args,
                    EnvironmentVariables.ByteShelfRoot.GetValueOrDefault(),
                    new ConsolePasswordReader(),
                    loggerFactory).GetAwaiter().GetResult();
                return;
            }

            if (ReplicationCommand.Flags.Any(args.Contains))
            {
                // Beside BYTESHELF_ROOT only APP_BASE_URL is read, by --replication-code.
                using ILoggerFactory loggerFactory = CreateStartupLoggerFactory();
                Environment.ExitCode = ReplicationCommand.RunAsync(
                    args,
                    EnvironmentVariables.ByteShelfRoot.GetValueOrDefault(),
                    EnvironmentVariables.AppBaseUrl.GetValueOrDefault(),
                    loggerFactory).GetAwaiter().GetResult();
                return;
            }

            RunApplication(args);
        }

        /// <summary>
        /// Boots controllers + Swagger with a dummy JWT secret and no-op auth stubs so the full
        /// OpenAPI document (including the auth endpoints) can be emitted with <b>no database, no
        /// environment validation, and no migrations</b>. This is what makes web UI codegen work
        /// on a fresh clone in about a second. Anything Swagger needs beyond controllers must also be
        /// registered here, or the emitted spec is silently incomplete.
        /// </summary>
        private static void RunSchemaOnlyMode(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            AddApi(builder.Services);

            // A dummy secret and no-op stores, only so the auth endpoints appear in the spec.
            string dummyJwtSecret = "schema-only-dummy-secret-not-used-for-real-auth";
            builder.Services.AddEasyReasyAuth(dummyJwtSecret, AuthSetup.Configure);
            builder.Services.AddRefreshTokenService<NoOpRefreshTokenStore>();
            builder.Services.AddScoped<IAuthRequestValidationService, NoOpAuthRequestValidationService>();

            WebApplication app = builder.Build();

            app.UseSwagger();
            app.UseSwaggerUI();
            AuthSetup.MapAuthEndpoints(app);
            app.MapControllers();

            app.Run();
        }

        /// <summary>
        /// Runs the dbup migrations and exits, instead of starting the long-running server. Only
        /// BYTESHELF_ROOT is required. The server still migrates at normal startup, which is a no-op
        /// once this has run. A failed migration throws, exiting the process non-zero.
        /// </summary>
        private static void RunMigrateOnlyMode()
        {
            using ILoggerFactory loggerFactory = CreateStartupLoggerFactory();
            ILogger<Program> logger = loggerFactory.CreateLogger<Program>();

            DatabaseMigrator.MigrateRoot(EnvironmentVariables.ByteShelfRoot.GetValue(), logger);
            logger.LogInformation("Migrate-only run complete.");
        }

        private static void RunApplication(string[] args)
        {
            EnvironmentVariableHelper.ValidateVariableNamesIn(typeof(EnvironmentVariables));

            WebApplicationBuilder builder = CreateBuilder(args);
            RootFolder root = RootFolder.Open(EnvironmentVariables.ByteShelfRoot.GetValue());
            WebApplication app = BuildApplication(builder, ServerSettings.FromEnvironment(root, builder.Environment));
            app.Run();
        }

        /// <summary>
        /// The features that hold and change the data, which the service tests build on their own; the
        /// web host adds the rest (live updates, version, replication, MCP, rate limits) beside them.
        /// Nothing is made or opened at the root.
        /// </summary>
        internal static void AddDomainServices(IServiceCollection services, ServerSettings settings)
        {
            services.AddSingleton(settings.PublicAddress);
            services.AddDatabaseServices(settings.Root);
            services.AddEmailServices(settings.Email);
            services.AddUserServices(settings.JwtSecret);
            services.AddJobServices();
            services.AddChangeLogServices();
            services.AddStorageServices(settings.Storage);
            services.AddMetadataServices();
            services.AddAccessServices();
        }

        /// <summary>The builder the server is built with, before anything of its own is registered.</summary>
        internal static WebApplicationBuilder CreateBuilder(string[] args)
        {
            // The content root is the executable's folder, which holds wwwroot/ and appsettings.json, not the
            // working directory, which is wherever the server was started from.
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
            builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestLineSize = MaxRequestLineBytes);
            return builder;
        }

        /// <summary>
        /// Every service of the server's own, which plugins' registrations are then added to, by the server
        /// and by <c>--check-plugin</c> alike. Nothing is made, opened or started.
        /// </summary>
        internal static IMvcBuilder AddServerServices(IServiceCollection services, ServerSettings settings)
        {
            IMvcBuilder mvc = AddApi(services);
            ConfigureForwardedHeaders(services, settings.TrustedProxies);

            // One line per feature; adding a feature = add a folder, add one line here or in AddDomainServices.
            AddDomainServices(services, settings);
            services.AddInitialAdmin(settings.InitialAdmin);
            services.AddInvalidationServices();
            services.AddVersionServices();
            services.AddReplicationServices(settings.Replication);
            services.AddMcpServices();
            services.AddRateLimitingServices();
            services.AddPluginServices();
            return mvc;
        }

        /// <summary>
        /// Migrates the root's database and builds the full application on it. Separate from
        /// <see cref="RunApplication"/> so the in-process HTTP tests build the same pipeline on a root
        /// and settings of their own; <paramref name="configureForTests"/> runs once every service is
        /// registered, so a test can replace or remove one.
        /// </summary>
        internal static WebApplication BuildApplication(WebApplicationBuilder builder, ServerSettings settings, Action<WebApplicationBuilder>? configureForTests = null)
        {
            using ILoggerFactory startupLoggerFactory = CreateStartupLoggerFactory();
            ILogger<Program> startupLogger = startupLoggerFactory.CreateLogger<Program>();
            DatabaseMigrator.RunMigrations(settings.Root.ConnectionString, startupLogger);

            IMvcBuilder mvc = AddServerServices(builder.Services, settings);
            mvc.LoadPlugins(settings.PluginsPath, startupLogger);
            configureForTests?.Invoke(builder);

            WebApplication app = builder.Build();

            // First, so everything after it sees the client's address rather than the proxy's.
            app.UseForwardedHeaders();

            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = StaticFileCachePolicy.Apply,
            });
            app.UsePluginAssets();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseMiddleware<ExceptionHandlingMiddleware>();

            app.UseRouting();

            app.UseEasyReasyAuth();

            app.UseAuthorization();

            // After the auth pipeline: the global limiter partitions by authenticated user name,
            // falling back to remote IP for anonymous requests.
            app.UseRateLimiter();

            AuthSetup.MapAuthEndpoints(app);

            app.MapControllers();

            app.MapAgentEndpoints();

            StaticFileCachePolicy.MapSpaFallback(app);

            return app;
        }

        /// <summary>
        /// Trusts <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> from the proxies listed in
        /// <paramref name="trustedProxies"/> (loopback is always trusted). Not <c>X-Forwarded-Host</c>: only
        /// a Development server without <c>APP_BASE_URL</c> reads the host, and a proxy passing one through
        /// from the client would let the client choose it.
        /// </summary>
        private static void ConfigureForwardedHeaders(IServiceCollection services, string? trustedProxies)
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                foreach (string entry in (trustedProxies ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (entry.Contains('/', StringComparison.Ordinal))
                    {
                        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(entry));
                    }
                    else
                    {
                        options.KnownProxies.Add(IPAddress.Parse(entry));
                    }
                }
            });
        }

        private static IMvcBuilder AddApi(IServiceCollection services)
        {
            // The assembly is named because MVC otherwise looks for controllers in the entry assembly,
            // which is the test runner when the HTTP tests build this pipeline.
            IMvcBuilder mvc = services.AddControllers()
                .AddApplicationPart(typeof(Program).Assembly)
                .AddJsonOptions(options => JsonDefaults.Configure(options.JsonSerializerOptions));

            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(options =>
            {
                // Non-nullable reference types emit as required/non-nullable in the spec, so the
                // generated TypeScript types match the C# nullability contract.
                options.SupportNonNullableReferenceTypes();

                // A query bound to an object (the search's FileSearch) is described by its property
                // names; camelCase, as the JSON bodies are. Binding ignores case, so both work.
                options.DescribeAllParametersInCamelCase();

                // A cursor travels as the text it writes (bytes:sha256), in a query and in JSON alike,
                // not as the record it is in C#.
                options.MapType<DuplicateCursor>(() => new OpenApiSchema { Type = "string" });
            });

            return mvc;
        }

        /// <summary>A console logger for the work done before the application's own logging exists.</summary>
        private static ILoggerFactory CreateStartupLoggerFactory()
        {
            return LoggerFactory.Create(config => config.AddConsole().SetMinimumLevel(LogLevel.Information));
        }
    }
}
