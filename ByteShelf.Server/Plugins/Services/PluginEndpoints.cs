namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>The endpoints controllers become when the server starts, where a bad route or action shows.</summary>
    internal static class PluginEndpoints
    {
        /// <summary>
        /// Builds every controller's endpoints from <paramref name="services"/> and throws them away. The
        /// server builds them as it starts, where what throws stops it, so a plugin's are tried first.
        /// </summary>
        public static void Try(IServiceCollection services)
        {
            using ServiceProvider provider = services.BuildServiceProvider();
            RouteBuilder routes = new RouteBuilder(provider);
            routes.MapControllers();
            foreach (EndpointDataSource source in routes.DataSources)
            {
                _ = source.Endpoints;
            }
        }

        private sealed class RouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
        {
            public IServiceProvider ServiceProvider { get; } = services;

            public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

            public IApplicationBuilder CreateApplicationBuilder()
            {
                return new ApplicationBuilder(ServiceProvider);
            }
        }
    }
}
