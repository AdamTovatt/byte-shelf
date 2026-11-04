namespace ByteShelfCommon
{
    /// <summary>The rules for a user's email and display name.</summary>
    public static class UserFields
    {
        /// <summary>The most characters an email can have.</summary>
        public const int MaxEmailLength = 254;

        /// <summary>The most characters a display name can have, once trimmed.</summary>
        public const int MaxDisplayNameLength = 100;

        /// <summary>Throws unless <paramref name="email"/> looks like an email address and fits.</summary>
        /// <exception cref="ArgumentException">It does not.</exception>
        public static void ValidateEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal) || email.Length > MaxEmailLength)
            {
                throw new ArgumentException($"A valid email address of at most {MaxEmailLength} characters is required.");
            }
        }

        /// <summary>The display name trimmed, as <see cref="DisplayText.Validate"/> allows it.</summary>
        /// <exception cref="ArgumentException">The name breaks the rule.</exception>
        public static string NormalizeDisplayName(string displayName)
        {
            return DisplayText.Validate(displayName, MaxDisplayNameLength, "A display name");
        }
    }
}
