using ByteShelf.Server.Email.Services;
using ByteShelf.Server.Shared;

namespace ByteShelf.Server.Email
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>Registers the one <see cref="IEmailSender"/> the kind of <paramref name="options"/> names, and the startup warning.</summary>
        public static IServiceCollection AddEmailServices(this IServiceCollection services, EmailOptions options)
        {
            services.AddSingleton(options);

            switch (options)
            {
                case PostmarkEmail postmark:
                    services.AddSingleton(postmark);
                    services.AddHttpClient(PostmarkEmailSender.HttpClientName, client => client.BaseAddress = PostmarkEmailSender.ApiBaseAddress);
                    services.AddSingleton<IEmailSender, PostmarkEmailSender>();
                    break;
                case DevMailEmail devMail:
                    services.AddSingleton(devMail);
                    services.TryAddTimeProvider();
                    services.AddSingleton<IEmailSender, DevMailEmailSender>();
                    break;
                case LogOnlyEmail:
                    services.AddSingleton<IEmailSender, LoggingEmailSender>();
                    break;
                default:
                    throw new InvalidOperationException($"No email sender is defined for {options.GetType().Name}.");
            }

            services.AddHostedService<EmailSenderStartupWarning>();

            return services;
        }
    }
}
