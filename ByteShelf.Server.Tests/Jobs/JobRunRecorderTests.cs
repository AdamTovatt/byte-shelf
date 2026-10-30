using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Repositories;
using ByteShelf.Server.Jobs.Services;
using ByteShelf.Server.Tests.TestUtilities;
using static ByteShelf.Server.Tests.TestUtilities.TestInstants;

namespace ByteShelf.Server.Tests.Jobs
{
    public class JobRunRecorderTests
    {
        private readonly Mock<IJobRunRepository> _repository = new Mock<IJobRunRepository>();
        private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(Now);

        private JobRunRecorder CreateRecorder(TimeProvider? timeProvider = null)
        {
            _repository
                .Setup(repository => repository.InsertStartedAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), null))
                .ReturnsAsync(Guid.NewGuid());
            _repository
                .Setup(repository => repository.CompleteAsync(
                    It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), null))
                .ReturnsAsync(true);

            return new JobRunRecorder(_repository.Object, timeProvider ?? _timeProvider, NullLogger<JobRunRecorder>.Instance);
        }

        private record TestSummary(int Processed);

        [Fact]
        public async Task RunAsync_SuccessfulWork_CompletesWithSummaryAndNullError()
        {
            JobRunRecorder recorder = CreateRecorder();

            await recorder.RunAsync(
                "test.job",
                _ => Task.FromResult(new JobRunOutcome<TestSummary>(Success: true, new TestSummary(5), ErrorMessage: null)),
                CancellationToken.None);

            _repository.Verify(repository => repository.InsertStartedAsync("test.job", Now, null), Times.Once);
            _repository.Verify(repository => repository.CompleteAsync(
                It.IsAny<Guid>(), Now, true, "{\"processed\":5}", null, null), Times.Once);
        }

        [Fact]
        public async Task RunAsync_WorkThrows_RecordsFailureAndRethrows()
        {
            JobRunRecorder recorder = CreateRecorder();

            await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.RunAsync<TestSummary>(
                "test.job",
                _ => throw new InvalidOperationException("boom"),
                CancellationToken.None));

            _repository.Verify(repository => repository.CompleteAsync(
                It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), false, null, "boom", null), Times.Once);
        }

        [Fact]
        public async Task RunAsync_CancelledAtShutdown_RecordsShutdownSentinelAndRethrows()
        {
            JobRunRecorder recorder = CreateRecorder();
            using CancellationTokenSource shutdownSource = new CancellationTokenSource();
            await shutdownSource.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(() => recorder.RunAsync<TestSummary>(
                "test.job",
                cancellationToken => throw new OperationCanceledException(cancellationToken),
                shutdownSource.Token));

            _repository.Verify(repository => repository.CompleteAsync(
                It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), false, null, JobRunSentinels.ShutdownErrorMessage, null), Times.Once);
        }

        [Fact]
        public async Task RunAsync_ClockRetreatsMidRun_ClampsCompletionToStart()
        {
            // FakeTimeProvider refuses to move backwards, so simulate the NTP retreat with a
            // sequence mock: the start stamp reads Now, the completion stamp reads five minutes
            // earlier.
            Mock<TimeProvider> retreatingClock = new Mock<TimeProvider>();
            retreatingClock
                .SetupSequence(provider => provider.GetUtcNow())
                .Returns(Now)
                .Returns(Now.AddMinutes(-5));
            JobRunRecorder recorder = CreateRecorder(retreatingClock.Object);

            await recorder.RunAsync(
                "test.job",
                _ => Task.FromResult(new JobRunOutcome<TestSummary>(Success: true, Summary: null, ErrorMessage: null)),
                CancellationToken.None);

            _repository.Verify(repository => repository.CompleteAsync(
                It.IsAny<Guid>(), Now, true, null, null, null), Times.Once);
        }

        [Fact]
        public async Task RunAsync_FailureWithBlankMessage_CoalescesToSentinel()
        {
            JobRunRecorder recorder = CreateRecorder();

            await recorder.RunAsync(
                "test.job",
                _ => Task.FromResult(new JobRunOutcome<TestSummary>(Success: false, Summary: null, ErrorMessage: "  ")),
                CancellationToken.None);

            _repository.Verify(repository => repository.CompleteAsync(
                It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), false, null, "Job failed without an error message.", null), Times.Once);
        }

        [Fact]
        public async Task RunAsync_CancelledByAnotherToken_RecordsAFailureNotAShutdown()
        {
            JobRunRecorder recorder = CreateRecorder();
            using CancellationTokenSource otherSource = new CancellationTokenSource();
            await otherSource.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(() => recorder.RunAsync<TestSummary>(
                "test.job",
                _ => throw new OperationCanceledException(otherSource.Token),
                CancellationToken.None));

            _repository.Verify(repository => repository.CompleteAsync(
                It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), false, null, It.Is<string>(message => message != JobRunSentinels.ShutdownErrorMessage), null), Times.Once);
        }
    }
}
