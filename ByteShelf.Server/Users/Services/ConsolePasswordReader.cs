using ByteShelf.Tools;

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

            // Ctrl+C ends the process, as in any command; nothing is changed before the password is read.
            string password = HiddenInput.ReadLine(Console.Error, $"New password for {email}: ", CancellationToken.None);
            return HiddenInput.ReadLine(Console.Error, "Again: ", CancellationToken.None) == password
                ? password
                : throw new ArgumentException("The two passwords typed differ.");
        }
    }
}
