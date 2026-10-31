namespace ByteShelfCommon
{
    /// <summary>
    /// What counts as an acceptable password, for every path that sets one (user creation, a change, a
    /// reset) on the server and in clients that check before sending.
    /// </summary>
    public static class PasswordPolicy
    {
        /// <summary>The fewest characters a password can have.</summary>
        public const int MinimumLength = 8;

        /// <summary>The most characters a password can have, which bounds the work a request makes the hasher do.</summary>
        public const int MaximumLength = 256;

        /// <summary>Throws unless the password is acceptable.</summary>
        /// <exception cref="ArgumentException">It is not.</exception>
        public static void Validate(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength || password.Length > MaximumLength)
            {
                throw new ArgumentException($"A password has {MinimumLength} to {MaximumLength} characters.");
            }
        }
    }
}
