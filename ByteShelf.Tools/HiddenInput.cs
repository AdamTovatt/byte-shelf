using System.Text;

namespace ByteShelf.Tools
{
    /// <summary>
    /// A line typed at the console without being shown, for a password: the byteshelf CLI's and the
    /// server's own commands read secrets this way when a person is at the terminal.
    /// </summary>
    public static class HiddenInput
    {
        private static readonly TimeSpan KeyPollInterval = TimeSpan.FromMilliseconds(20);

        /// <summary>
        /// Writes <paramref name="prompt"/> to <paramref name="prompts"/> (stderr, so stdout stays the
        /// command's), then reads keys until Enter, keeping printable ones and honouring Backspace.
        /// </summary>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> fired while typing, as Ctrl+C does.</exception>
        public static string ReadLine(TextWriter prompts, string prompt, CancellationToken cancellationToken)
        {
            prompts.Write(prompt);
            StringBuilder typed = new StringBuilder();
            for (ConsoleKeyInfo key = ReadKey(cancellationToken); key.Key != ConsoleKey.Enter; key = ReadKey(cancellationToken))
            {
                if (key.Key == ConsoleKey.Backspace)
                {
                    typed.Length = Math.Max(typed.Length - 1, 0);
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    typed.Append(key.KeyChar);
                }
            }

            prompts.WriteLine();
            return typed.ToString();
        }

        /// <summary>The next key typed, waiting in a way a token can end: Console.ReadKey alone would not return for Ctrl+C.</summary>
        private static ConsoleKeyInfo ReadKey(CancellationToken cancellationToken)
        {
            while (!Console.KeyAvailable)
            {
                cancellationToken.WaitHandle.WaitOne(KeyPollInterval);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Console.ReadKey(intercept: true);
        }
    }
}
