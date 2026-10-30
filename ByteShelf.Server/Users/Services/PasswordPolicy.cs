namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// The single definition of what counts as an acceptable password, shared by every path that
    /// sets one (user creation, self-service change, reset).
    /// </summary>
    public static class PasswordPolicy
    {
        public const int MinimumLength = 8;

        /// <summary>Bounds the work a request can make the password hasher do.</summary>
        public const int MaximumLength = 256;

        /// <summary>Throws <see cref="ArgumentException"/> (answered as 400) when the password is unacceptable.</summary>
        public static void Validate(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength || password.Length > MaximumLength)
            {
                throw new ArgumentException($"A password has {MinimumLength} to {MaximumLength} characters.");
            }
        }
    }
}
