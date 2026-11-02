using System.Text;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// A new password from stdin when it is redirected (one line, as <c>&lt; file</c> or a pipe gives it),
    /// otherwise typed twice at the terminal without being shown.
    /// </summary>
    public sealed class ConsolePasswordReader : IPasswordReader
    {
        public string ReadNewPassword(string email)
        {
            if (Console.IsInputRedirected)
            {
                return Console.In.ReadLine() ?? throw new ArgumentException("No password came on stdin.");
            }

            string password = ReadHidden($"New password for {email}: ");
            return ReadHidden("Again: ") == password
                ? password
                : throw new ArgumentException("The two passwords typed differ.");
        }

        /// <summary>A line typed at the terminal, not echoed; the prompt goes to stderr, so stdout stays the command's.</summary>
        private static string ReadHidden(string prompt)
        {
            Console.Error.Write(prompt);
            StringBuilder typed = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    typed.Length = Math.Max(0, typed.Length - 1);
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    typed.Append(key.KeyChar);
                }
            }

            Console.Error.WriteLine();
            return typed.ToString();
        }
    }
}
