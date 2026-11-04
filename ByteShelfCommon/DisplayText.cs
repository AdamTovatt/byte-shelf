namespace ByteShelfCommon
{
    /// <summary>The rule for short names people read, such as a user's display name and a key's name.</summary>
    public static class DisplayText
    {
        /// <summary>
        /// Answers <paramref name="value"/> trimmed, which must then be 1 to <paramref name="maxLength"/>
        /// characters long without control characters.
        /// </summary>
        /// <param name="value">The name as given.</param>
        /// <param name="maxLength">The most characters the trimmed name can have.</param>
        /// <param name="subject">What the value is, to start the error with, such as "A key's name".</param>
        /// <exception cref="ArgumentException">The value breaks the rule.</exception>
        public static string Validate(string? value, int maxLength, string subject)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0 || trimmed.Length > maxLength)
            {
                throw new ArgumentException($"{subject} must be 1 to {maxLength} characters long.");
            }

            if (trimmed.Any(char.IsControl))
            {
                throw new ArgumentException($"{subject} cannot contain control characters.");
            }

            return trimmed;
        }
    }
}
