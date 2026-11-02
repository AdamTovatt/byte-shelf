namespace ByteShelf.Server.Shared.Root
{
    /// <summary>
    /// The arguments of one of the server's own commands (<see cref="RootCommand"/>), taken flag by flag;
    /// <see cref="AllTaken"/> then says whether they were well formed with nothing left over.
    /// </summary>
    /// <remarks>
    /// Its own few lines rather than the CLI's ArgumentList, which reads a command table and
    /// ByteShelf.Tools does not hold: a handful of flags, each with at most one value, do not earn moving it there.
    /// </remarks>
    internal sealed class CommandArguments
    {
        private readonly List<string> _left;
        private bool _wellFormed = true;

        public CommandArguments(IReadOnlyList<string> args)
        {
            _left = new List<string>(args);
        }

        /// <summary>Whether every argument was taken, and every flag that takes a value had one.</summary>
        public bool AllTaken => _wellFormed && _left.Count == 0;

        /// <summary>The value after <paramref name="flag"/>, taking both; null when the flag is not there or has no value.</summary>
        public string? Value(string flag)
        {
            int at = _left.IndexOf(flag);
            if (at < 0)
            {
                return null;
            }

            if (at + 1 >= _left.Count || _left[at + 1].StartsWith("--", StringComparison.Ordinal))
            {
                _wellFormed = false;
                _left.RemoveAt(at);
                return null;
            }

            string value = _left[at + 1];
            _left.RemoveRange(at, 2);
            return value;
        }

        /// <summary>Whether <paramref name="flag"/>, which takes no value, is there, taking it.</summary>
        public bool Flag(string flag)
        {
            return _left.Remove(flag);
        }
    }
}
