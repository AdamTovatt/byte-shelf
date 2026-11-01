using System.Net;
using ByteShelf.Server.Access;
using ByteShelf.Server.Backup.Services;
using ByteShelf.Server.ChangeLog;
using ByteShelf.Server.Email;
using ByteShelf.Server.Invalidation;
using ByteShelf.Server.Jobs;
using ByteShelf.Server.Replication;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Database;
using ByteShelf.Server.Shared.Json;
using ByteShelf.Server.Shared.Middleware;
using ByteShelf.Server.Shared.RateLimiting;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Shared.StaticFiles;
using ByteShelf.Server.Storage;
using ByteShelf.Server.Users;
using ByteShelf.Server.Version;
using EasyReasy.Auth;
using EasyReasy.EnvironmentVariables;
using Microsoft.AspNetCore.HttpOverrides;

namespace ByteShelf.Server
{
    public class Program
    {
        /// <summary>Where reset links point in Development when <c>APP_BASE_URL</c> is not set.</summary>
        private const string DevelopmentAppBaseUrl = "http://localhost:5190";

        public static void Main(string[] args)
        {
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
            builder.Services.AddEasyReasyAuth(dummyJwtSecret, options => options.Issuer = AuthSetup.JwtIssuer);
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

            RootFolder root = RootFolder.Open(EnvironmentVariables.ByteShelfRoot.GetValue());
            DatabaseMigrator.RunMigrations(root.ConnectionString, logger);
            logger.LogInformation("Migrate-only run complete.");
        }

        private static void RunApplication(string[] args)
        {
            EnvironmentVariableHelper.ValidateVariableNamesIn(typeof(EnvironmentVariables));

            WebApplication app = BuildApplication(args, RootFolder.Open(EnvironmentVariables.ByteShelfRoot.GetValue()));
            app.Run();
        }

        /// <summary>
        /// Migrates the root's database and builds the full application on it. Separate from
        /// <see cref="RunApplication"/> so the in-process HTTP tests build the same pipeline on a root
        /// of their own; <paramref name="configureForTests"/> runs once every service is registered, so a
        /// test can replace or remove one.
        /// </summary>
        internal static WebApplication BuildApplication(string[] args, RootFolder root, Action<WebApplicationBuilder>? configureForTests = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            using (ILoggerFactory loggerFactory = CreateStartupLoggerFactory())
            {
                DatabaseMigrator.RunMigrations(root.ConnectionString, loggerFactory.CreateLogger<Program>());
            }

            AddApi(builder.Services);
            ConfigureForwardedHeaders(builder.Services);

            // Domain services — one line per domain; adding a feature = add a folder, add one line.
            builder.Services.AddDatabaseServices(root);
            builder.Services.AddInvalidationServices();
            builder.Services.AddEmailServices();
            string appBaseUrl = ResolveAppBaseUrl(builder.Environment);
            builder.Services.AddUserServices(root, appBaseUrl);
            builder.Services.AddJobServices();
            builder.Services.AddVersionServices();
            builder.Services.AddChangeLogServices();
            builder.Services.AddStorageServices();
            builder.Services.AddAccessServices();
            builder.Services.AddReplicationServices(appBaseUrl);
            builder.Services.AddRateLimitingServices();
            configureForTests?.Invoke(builder);

            WebApplication app = builder.Build();

            // First, so everything after it sees the client's address rather than the proxy's.
            app.UseForwardedHeaders();

            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = StaticFileCachePolicy.Apply,
            });

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

            StaticFileCachePolicy.MapSpaFallback(app);

            return app;
        }

        /// <summary>
        /// The public URL reset links point at. Required outside Development: a link to localhost in a
        /// real user's inbox is a broken link.
        /// </summary>
        private static string ResolveAppBaseUrl(IHostEnvironment environment)
        {
            string? configured = EnvironmentVariables.AppBaseUrl.GetValueOrDefault();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("APP_BASE_URL must be set to the server's public URL outside Development.");
            }

            return DevelopmentAppBaseUrl;
        }

        /// <summary>
        /// Trusts <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> from the proxies listed in
        /// <c>BYTESHELF_TRUSTED_PROXIES</c> (loopback is always trusted).
        /// </summary>
        private static void ConfigureForwardedHeaders(IServiceCollection services)
        {
            string? trustedProxies = EnvironmentVariables.TrustedProxies.GetValueOrDefault();

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

        private static void AddApi(IServiceCollection services)
        {
            // The assembly is named because MVC otherwise looks for controllers in the entry assembly,
            // which is the test runner when the HTTP tests build this pipeline.
            services.AddControllers()
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
            });
        }

        /// <summary>A console logger for the work done before the application's own logging exists.</summary>
        private static ILoggerFactory CreateStartupLoggerFactory()
        {
            return LoggerFactory.Create(config => config.AddConsole().SetMinimumLevel(LogLevel.Information));
        }
    }
}
