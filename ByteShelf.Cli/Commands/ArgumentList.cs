namespace ByteShelf.Cli.Commands
{
    /// <summary>
    /// A command's words after its name: positional arguments, <c>--flags</c>, and options that take
    /// the next word as their value. <c>--</c> ends the options, for a path that starts with one.
    /// Anything a command does not ask for is refused by <see cref="EnsureAllUsed"/>.
    /// </summary>
    internal sealed class ArgumentList
    {
        /// <summary>The options that take a value; every other <c>--word</c> is a flag.</summary>
        private static readonly HashSet<string> ValueOptions = new HashSet<string>(StringComparer.Ordinal)
        {
            "--email", "--name", "--quota", "--type",
        };

        private readonly List<string> _positionals = new List<string>();
        private readonly Dictionary<string, string?> _options = new Dictionary<string, string?>(StringComparer.Ordinal);
        private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);
        private int _positionalsUsed;

        private ArgumentList()
        {
        }

        /// <exception cref="UsageException">An option is missing its value or given twice.</exception>
        public static ArgumentList Parse(IReadOnlyList<string> words)
        {
            ArgumentList arguments = new ArgumentList();
            bool optionsEnded = false;
            for (int i = 0; i < words.Count; i++)
            {
                string word = words[i];
                if (optionsEnded || !word.StartsWith("--", StringComparison.Ordinal))
                {
                    arguments._positionals.Add(word);
                }
                else if (word == "--")
                {
                    optionsEnded = true;
                }
                else if (arguments._options.ContainsKey(word))
                {
                    throw new UsageException($"{word} is given twice.");
                }
                else if (ValueOptions.Contains(word))
                {
                    if (i + 1 >= words.Count)
                    {
                        throw new UsageException($"{word} needs a value.");
                    }

                    arguments._options[word] = words[++i];
                }
                else
                {
                    arguments._options[word] = null;
                }
            }

            return arguments;
        }

        /// <exception cref="UsageException">There are fewer positional arguments.</exception>
        public string Required(int index, string name)
        {
            return Optional(index) ?? throw new UsageException($"Missing <{name}>.");
        }

        public string? Optional(int index)
        {
            _positionalsUsed = Math.Max(_positionalsUsed, index + 1);
            return index < _positionals.Count ? _positionals[index] : null;
        }

        public bool Flag(string name)
        {
            _used.Add(name);
            return _options.ContainsKey(name);
        }

        public string? Value(string name)
        {
            _used.Add(name);
            return _options.GetValueOrDefault(name);
        }

        /// <exception cref="UsageException">The value is not a whole number of bytes.</exception>
        public long? Bytes(string name)
        {
            string? value = Value(name);
            if (value == null)
            {
                return null;
            }

            return long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long bytes)
                ? bytes
                : throw new UsageException($"{name} takes a number of bytes, not '{value}'.");
        }

        /// <exception cref="UsageException">There is a positional argument or an option the command did not read.</exception>
        public void EnsureAllUsed()
        {
            if (_positionals.Count > _positionalsUsed)
            {
                throw new UsageException($"Unexpected '{_positionals[_positionalsUsed]}'.");
            }

            string? unknown = _options.Keys.FirstOrDefault(option => !_used.Contains(option));
            if (unknown != null)
            {
                throw new UsageException($"Unknown option {unknown}.");
            }
        }
    }
}
