namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>One plugin's turns in one lane: one holder at a time, the next the one waiting longest of the highest priority.</summary>
    internal sealed class PluginTurns
    {
        private readonly Lock _lock = new Lock();
        private readonly List<Turn> _waiting = new List<Turn>();
        private bool _taken;

        public PluginTurns(PluginLane lane)
        {
            Lane = lane;
        }

        /// <summary>The lane a holder's run takes.</summary>
        public PluginLane Lane { get; }

        /// <summary>Takes a turn, granted once <see cref="Turn.Granted"/> ends; dispose it to give it up, granted or not.</summary>
        public Turn Enter(PluginPriority priority)
        {
            Turn turn = new Turn(this, priority);
            lock (_lock)
            {
                _waiting.Add(turn);
                GrantNext();
            }

            return turn;
        }

        // Called under the lock. Granting runs nobody's code here: the source completes its waiters asynchronously.
        private void GrantNext()
        {
            if (!_taken && _waiting.MaxBy(turn => turn.Priority) is Turn next)
            {
                _taken = true;
                _waiting.Remove(next);
                next.Grant();
            }
        }

        internal sealed class Turn : IDisposable
        {
            private readonly PluginTurns _turns;
            private readonly TaskCompletionSource _granted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            private bool _ended;

            public Turn(PluginTurns turns, PluginPriority priority)
            {
                _turns = turns;
                Priority = priority;
            }

            public PluginPriority Priority { get; private set; }

            /// <summary>The lane its run takes.</summary>
            public PluginLane Lane => _turns.Lane;

            public Task Granted => _granted.Task;

            /// <summary>Waits from now on with <paramref name="priority"/>, if that is a higher one.</summary>
            public void RaiseTo(PluginPriority priority)
            {
                lock (_turns._lock)
                {
                    Priority = priority > Priority ? priority : Priority;
                }
            }

            public void Dispose()
            {
                lock (_turns._lock)
                {
                    if (_ended)
                    {
                        return;
                    }

                    _ended = true;
                    if (!_turns._waiting.Remove(this))
                    {
                        _turns._taken = false;
                    }

                    _turns.GrantNext();
                }
            }

            internal void Grant()
            {
                _granted.TrySetResult();
            }
        }
    }
}
