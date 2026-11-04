namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Where <see cref="UserCommand"/> reads a new password: never from its arguments, which other users
    /// of the machine can read in its process list and which stay in a shell's history.
    /// </summary>
    public interface IPasswordReader
    {
        /// <summary>The new password for <paramref name="email"/>.</summary>
        /// <exception cref="ArgumentException">None was given, or the two typed differ.</exception>
        string ReadNewPassword(string email);
    }
}
