using ByteShelf.Server.Email.Services;

namespace ByteShelf.Server.Email
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddEmailServices(this IServiceCollection services)
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
            services.AddHostedService<LoggingEmailSenderStartupWarning>();

            return services;
        }
    }
}
