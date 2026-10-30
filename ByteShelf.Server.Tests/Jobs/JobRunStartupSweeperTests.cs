using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Repositories;
using ByteShelf.Server.Jobs.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ByteShelf.Server.Tests.Jobs
{
    public class JobRunStartupSweeperTests
    {
        private readonly Mock<IJobRunRepository> _repository = new Mock<IJobRunRepository>();

        [Fact]
        public async Task StartAsync_SweepsOrphansWithUncleanShutdownSentinel()
        {
            _repository
                .Setup(r => r.CompleteOrphansAsync(JobRunSentinels.UncleanShutdownErrorMessage, null))
                .ReturnsAsync(2);
            await using ServiceProvider provider = BuildProvider();

            await CreateSweeper(provider).StartAsync(CancellationToken.None);

            _repository.Verify(r => r.CompleteOrphansAsync(JobRunSentinels.UncleanShutdownErrorMessage, null), Times.Once);
        }

        [Fact]
        public async Task StartAsync_RepositoryThrows_DoesNotAbortStartup()
        {
            _repository
                .Setup(r => r.CompleteOrphansAsync(It.IsAny<string>(), null))
                .ThrowsAsync(new InvalidOperationException("database unavailable"));
            await using ServiceProvider provider = BuildProvider();

            Exception? exception = await Record.ExceptionAsync(() => CreateSweeper(provider).StartAsync(CancellationToken.None));

            Assert.Null(exception);
        }

        [Fact]
        public async Task StartAsync_AlreadyCancelled_ThrowsWithoutSweeping()
        {
            await using ServiceProvider provider = BuildProvider();
            using CancellationTokenSource cancelledSource = new CancellationTokenSource();
            await cancelledSource.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(() => CreateSweeper(provider).StartAsync(cancelledSource.Token));

            _repository.Verify(r => r.CompleteOrphansAsync(It.IsAny<string>(), null), Times.Never);
        }

        private ServiceProvider BuildProvider()
        {
            ServiceCollection services = new ServiceCollection();
            services.AddScoped(_ => _repository.Object);
            return services.BuildServiceProvider();
        }

        private static JobRunStartupSweeper CreateSweeper(ServiceProvider provider)
        {
            return new JobRunStartupSweeper(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<JobRunStartupSweeper>.Instance);
        }
    }
}
