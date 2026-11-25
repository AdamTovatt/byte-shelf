using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Tests.Plugins.Processes
{
    /// <summary>Who is next when one plugin has several things to make.</summary>
    public class PluginTurnsTests
    {
        private readonly PluginTurns _turns = new PluginTurns();

        [Fact]
        public void Enter_OneAtATime_HighestPriorityThenLongestWaiting()
        {
            PluginTurns.Turn first = _turns.Enter(PluginPriority.Backlog);
            PluginTurns.Turn backlog = _turns.Enter(PluginPriority.Backlog);
            PluginTurns.Turn stored = _turns.Enter(PluginPriority.Stored);
            PluginTurns.Turn asked = _turns.Enter(PluginPriority.Asked);
            PluginTurns.Turn askedLater = _turns.Enter(PluginPriority.Asked);
            Assert.True(first.Granted.IsCompleted);

            foreach ((PluginTurns.Turn ending, PluginTurns.Turn next) in new[] { (first, asked), (asked, askedLater), (askedLater, stored), (stored, backlog) })
            {
                Assert.False(next.Granted.IsCompleted);
                ending.Dispose();
                Assert.True(next.Granted.IsCompleted);
            }
        }

        [Fact]
        public void Turn_RaisedWhileWaiting_GoesAheadAndNeverBack()
        {
            PluginTurns.Turn first = _turns.Enter(PluginPriority.Backlog);
            PluginTurns.Turn stored = _turns.Enter(PluginPriority.Stored);
            PluginTurns.Turn raised = _turns.Enter(PluginPriority.Backlog);
            raised.RaiseTo(PluginPriority.Asked);
            raised.RaiseTo(PluginPriority.Backlog);

            first.Dispose();

            Assert.Equal((true, false), (raised.Granted.IsCompleted, stored.Granted.IsCompleted));
        }

        [Fact]
        public void Dispose_AWaitingTurn_LeavesTheQueueAndTwiceChangesNothing()
        {
            PluginTurns.Turn first = _turns.Enter(PluginPriority.Backlog);
            PluginTurns.Turn left = _turns.Enter(PluginPriority.Asked);
            PluginTurns.Turn next = _turns.Enter(PluginPriority.Backlog);
            left.Dispose();
            left.Dispose();
            Assert.False(next.Granted.IsCompleted);

            first.Dispose();
            first.Dispose();

            Assert.True(next.Granted.IsCompleted);
            Assert.False(left.Granted.IsCompleted);
            Assert.False(_turns.Enter(PluginPriority.Asked).Granted.IsCompleted);
        }
    }
}
