using ByteShelf.Server.Derived.Services;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>Who is next when one plugin has several things to make.</summary>
    public class MakingTurnsTests
    {
        private readonly MakingTurns _turns = new MakingTurns();

        [Fact]
        public void Enter_OneAtATime_HighestPriorityThenLongestWaiting()
        {
            MakingTurns.Turn first = _turns.Enter(MakingPriority.Backlog);
            MakingTurns.Turn backlog = _turns.Enter(MakingPriority.Backlog);
            MakingTurns.Turn stored = _turns.Enter(MakingPriority.Stored);
            MakingTurns.Turn asked = _turns.Enter(MakingPriority.Asked);
            MakingTurns.Turn askedLater = _turns.Enter(MakingPriority.Asked);
            Assert.True(first.Granted.IsCompleted);

            foreach ((MakingTurns.Turn ending, MakingTurns.Turn next) in new[] { (first, asked), (asked, askedLater), (askedLater, stored), (stored, backlog) })
            {
                Assert.False(next.Granted.IsCompleted);
                ending.Dispose();
                Assert.True(next.Granted.IsCompleted);
            }
        }

        [Fact]
        public void Turn_RaisedWhileWaiting_GoesAheadAndNeverBack()
        {
            MakingTurns.Turn first = _turns.Enter(MakingPriority.Backlog);
            MakingTurns.Turn stored = _turns.Enter(MakingPriority.Stored);
            MakingTurns.Turn raised = _turns.Enter(MakingPriority.Backlog);
            raised.RaiseTo(MakingPriority.Asked);
            raised.RaiseTo(MakingPriority.Backlog);

            first.Dispose();

            Assert.Equal((true, false), (raised.Granted.IsCompleted, stored.Granted.IsCompleted));
        }

        [Fact]
        public void Dispose_AWaitingTurn_LeavesTheQueueAndTwiceChangesNothing()
        {
            MakingTurns.Turn first = _turns.Enter(MakingPriority.Backlog);
            MakingTurns.Turn left = _turns.Enter(MakingPriority.Asked);
            MakingTurns.Turn next = _turns.Enter(MakingPriority.Backlog);
            left.Dispose();
            left.Dispose();
            Assert.False(next.Granted.IsCompleted);

            first.Dispose();
            first.Dispose();

            Assert.True(next.Granted.IsCompleted);
            Assert.False(left.Granted.IsCompleted);
            Assert.False(_turns.Enter(MakingPriority.Asked).Granted.IsCompleted);
        }
    }
}
