using ByteShelf.Server.Email;
using ByteShelf.Server.Email.Services;
using ByteShelf.Server.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ByteShelf.Server.Tests.Email
{
    /// <summary>Which sender each kind of settings registers, built in the real container.</summary>
    public sealed class EmailServicesTests
    {
        private static readonly MailSender From = new MailSender("shelf@example.com", "Shelf");

        [Fact]
        public void AddEmailServices_LogOnly_OnlyLogs()
        {
            using ServiceProvider provider = Build(EmailOptions.None);

            Assert.IsType<LoggingEmailSender>(provider.GetRequiredService<IEmailSender>());
        }

        [Fact]
        public void AddEmailServices_DevMail_WritesThere()
        {
            using ServiceProvider provider = Build(new DevMailEmail("/nowhere/mail", From));

            Assert.IsType<DevMailEmailSender>(provider.GetRequiredService<IEmailSender>());
        }

        [Fact]
        public void AddEmailServices_Postmark_SendsThroughItsApi()
        {
            using ServiceProvider provider = Build(new PostmarkEmail("server-token", From));

            Assert.IsType<PostmarkEmailSender>(provider.GetRequiredService<IEmailSender>());
            HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(PostmarkEmailSender.HttpClientName);
            Assert.Equal(new Uri("https://api.postmarkapp.com/"), client.BaseAddress);
        }

        private static ServiceProvider Build(EmailOptions options)
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(HostEnvironments.Named(Environments.Development));
            services.AddEmailServices(options);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        }
    }
}
