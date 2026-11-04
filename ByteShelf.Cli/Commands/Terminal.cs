using ByteShelf.Tools;

namespace ByteShelf.Cli.Commands
{
    /// <summary>Where a command reads and writes: the console, or what a test hands it.</summary>
    internal sealed class Terminal
    {
        /// <summary>The ways <see cref="ReadSecret"/> takes a secret, for a message saying none came.</summary>
        public const string HowToGive = "type it at a terminal, or give it as a line of standard input";

        private readonly Func<Stream> _openInput;
        private readonly Func<Stream> _openOutput;

        /// <param name="input">Text read from standard input, such as a password piped in.</param>
        /// <param name="output">Results.</param>
        /// <param name="error">Errors and prompts, so results can be piped on their own.</param>
        /// <param name="interactive">Whether a person is typing, so a secret is read without echo.</param>
        /// <param name="openInput">Standard input as bytes, for <c>put -</c>.</param>
        /// <param name="openOutput">Standard output as bytes, for <c>get … -</c>.</param>
        public Terminal(TextReader input, TextWriter output, TextWriter error, bool interactive, Func<Stream> openInput, Func<Stream> openOutput)
        {
            Input = input;
            Output = output;
            Error = error;
            Interactive = interactive;
            _openInput = openInput;
            _openOutput = openOutput;
        }

        public TextReader Input { get; }

        public TextWriter Output { get; }

        public TextWriter Error { get; }

        public bool Interactive { get; }

        public static Terminal FromConsole()
        {
            return new Terminal(
                Console.In,
                Console.Out,
                Console.Error,
                !Console.IsInputRedirected,
                Console.OpenStandardInput,
                Console.OpenStandardOutput);
        }

        public Stream OpenInput()
        {
            return _openInput();
        }

        public Stream OpenOutput()
        {
            return _openOutput();
        }

        /// <summary>
        /// Reads a password or key: typed without echo after <paramref name="prompt"/> when a person is
        /// at the terminal, else the first line of standard input. Never an argument, which would land
        /// in the shell's history.
        /// </summary>
        /// <param name="missing">What to say when nothing was given, usually <see cref="NotGiven"/>.</param>
        /// <exception cref="UsageException">Nothing was given.</exception>
        /// <exception cref="OperationCanceledException">Ctrl+C was pressed while typing.</exception>
        public string ReadSecret(string prompt, string missing, CancellationToken cancellationToken)
        {
            string? secret = Interactive ? HiddenInput.ReadLine(Error, prompt, cancellationToken) : Input.ReadLine();
            return string.IsNullOrEmpty(secret) ? throw new UsageException(missing) : secret;
        }

        /// <summary>
        /// That no <paramref name="what"/> was given, and the ways <see cref="ReadSecret"/> takes one,
        /// followed by <paramref name="orElse"/>, another way a command takes it.
        /// </summary>
        public static string NotGiven(string what, string orElse = "")
        {
            return $"No {what} was given: {HowToGive}{orElse}.";
        }
    }
}
