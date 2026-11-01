using ByteShelf.Server.Replication.Controllers;
using ByteShelf.Server.Replication.Repositories;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Replication
{
    public static class ServiceCollectionExtensions
    {
        /// <param name="services">The application's services.</param>
        /// <param name="publicUrl">This server's public URL, which pairing codes made here name.</param>
        public static IServiceCollection AddReplicationServices(this IServiceCollection services, string publicUrl)
        {
            services.TryAddTimeProvider();

            services.AddSingleton(new ReplicationOptions { PublicUrl = publicUrl });
            services.AddSingleton<ReplicaSyncControl>();

            // A redirect would carry the replica's credential header to wherever it points (unlike
            // Authorization, a custom header is not dropped), so none is followed.
            services.AddHttpClient(PrimaryClient.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler((handler, _) =>
                {
                    switch (handler)
                    {
                        case SocketsHttpHandler sockets:
                            sockets.AllowAutoRedirect = false;
                            break;
                        case HttpClientHandler client:
                            client.AllowAutoRedirect = false;
                            break;
                    }
                });

            services.AddScoped<IServerInstanceRepository, ServerInstanceRepository>();
            services.AddScoped<IReplicaRepository, ReplicaRepository>();
            services.AddScoped<IReplicationSourceRepository, ReplicationSourceRepository>();
            services.AddScoped<IReplicationCopyRepository, ReplicationCopyRepository>();
            services.AddScoped<ReplicationState>();
            services.AddScoped<PrimaryClient>();
            services.AddScoped<ReplicaApplier>();
            services.AddScoped<ReplicationPairing>();
            services.AddScoped<IReplicationFeed, ReplicationFeed>();
            services.AddScoped<ReplicaSync>();
            services.AddScoped<ReplicaReadOnlyFilter>();

            services.Configure<MvcOptions>(options => options.Filters.AddService<ReplicaReadOnlyFilter>());
            services.AddHostedService<ReplicaSyncWorker>();

            return services;
        }
    }
}
