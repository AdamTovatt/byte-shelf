using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Jobs.Repositories;
using ByteShelf.Server.Tests.TestUtilities;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Tests.Jobs
{
    public class JobRunRepositoryTests : TransactionalRepositoryTestBase
    {
        private JobRunRepository _repository = null!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _repository = new JobRunRepository(DataSource, SessionFactory);
        }

        [Fact]
        public async Task InsertStartedAsync_ThenComplete_StoresTheOutcome()
        {
            Guid runId = await _repository.InsertStartedAsync("test.roundtrip", TestInstants.Now, Session);

            bool completed = await _repository.CompleteAsync(
                runId, TestInstants.Now.AddSeconds(30), success: true, resultSummaryJson: "{\"count\":3}", errorMessage: null, Session);

            Assert.True(completed);
            RunRow run = await ReadRunAsync(runId);
            Assert.True(run.Success);
            Assert.Equal("{\"count\":3}", run.ResultSummary);
            Assert.Null(run.ErrorMessage);
            Assert.Equal(TestInstants.Now.AddSeconds(30), run.CompletedAt);
        }

        [Fact]
        public async Task CompleteAsync_UnknownRunId_ReturnsFalse()
        {
            bool completed = await _repository.CompleteAsync(
                Guid.NewGuid(), TestInstants.Now, success: true, resultSummaryJson: null, errorMessage: null, Session);

            Assert.False(completed);
        }

        [Fact]
        public async Task CompleteOrphansAsync_StampsOpenRowsWithTheSentinelAndTheirStartTime()
        {
            Guid orphanId = await _repository.InsertStartedAsync("test.orphan", TestInstants.Now, Session);

            int swept = await _repository.CompleteOrphansAsync(JobRunSentinels.UncleanShutdownErrorMessage, Session);

            Assert.Equal(1, swept);
            RunRow run = await ReadRunAsync(orphanId);
            Assert.False(run.Success);
            Assert.Equal(JobRunSentinels.UncleanShutdownErrorMessage, run.ErrorMessage);
            Assert.Equal(TestInstants.Now, run.CompletedAt);
        }

        private async Task<RunRow> ReadRunAsync(Guid id)
        {
            return await Session.Connection.QuerySingleAsync<RunRow>(
                "SELECT success, result_summary AS ResultSummary, error_message AS ErrorMessage, completed_at AS CompletedAt FROM job_run WHERE id = @id",
                new { id },
                transaction: Session.Transaction);
        }

        internal sealed class RunRow
        {
            public bool? Success { get; set; }
            public string? ResultSummary { get; set; }
            public string? ErrorMessage { get; set; }
            public DateTimeOffset? CompletedAt { get; set; }
        }
    }
}
