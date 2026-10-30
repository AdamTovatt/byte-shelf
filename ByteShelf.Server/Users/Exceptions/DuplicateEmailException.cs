using ByteShelf.Server.Shared.Errors;

namespace ByteShelf.Server.Users.Exceptions
{
    /// <summary>
    /// Thrown when creating a user with an email that already exists (case-insensitive). A conflict
    /// like any other, so it answers 409 through <see cref="ConflictException"/>'s mapping.
    /// </summary>
    public class DuplicateEmailException : ConflictException
    {
        public DuplicateEmailException(string email)
            : base($"A user with the email '{email}' already exists.")
        {
        }
    }
}
