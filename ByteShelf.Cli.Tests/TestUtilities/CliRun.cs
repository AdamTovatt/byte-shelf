using ByteShelf.Cli.Commands;
using ByteShelf.Cli.Connection;

namespace ByteShelf.Cli.Tests.TestUtilities
{
    /// <summary>One command line run through <see cref="CliApp"/>, with what it printed.</summary>
    internal sealed class CliRun
    {
        private CliRun(int exitCode, string output, string error, byte[] binaryOutput)
        {
            ExitCode = exitCode;
            Output = output;
            Error = error;
            BinaryOutput = binaryOutput;
        }

        public int ExitCode { get; }

        public string Output { get; }

        public string Error { get; }

        /// <summary>What was written to standard output as bytes, as <c>get … -</c> does.</summary>
        public byte[] BinaryOutput { get; }

        /// <param name="clients">Where the commands get their client.</param>
        /// <param name="commandLine">The words after <c>byteshelf</c>.</param>
        /// <param name="input">Standard input, as text lines and as bytes.</param>
        /// <param name="cancellationToken">Stands for Ctrl+C.</param>
        /// <param name="encryptionWorkFactor">The work factor the CLI encrypts with; null for its own default.</param>
        public static async Task<CliRun> RunAsync(IClientSource clients, string[] commandLine, string input = "", CancellationToken cancellationToken = default, int? encryptionWorkFactor = null)
        {
            using StringReader inputText = new StringReader(input);
            using StringWriter output = new StringWriter();
            using StringWriter error = new StringWriter();
            using MemoryStream binaryOutput = new MemoryStream();
            Terminal terminal = new Terminal(
                inputText,
                output,
                error,
                interactive: false,
                () => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(input)),
                () => binaryOutput);

            CliApp app = encryptionWorkFactor is int workFactor ? new CliApp(clients, terminal, workFactor) : new CliApp(clients, terminal);
            int exitCode = await app.RunAsync(commandLine, cancellationToken);
            return new CliRun(exitCode, output.ToString().TrimEnd(), error.ToString().TrimEnd(), binaryOutput.ToArray());
        }
    }
}
