using System.Diagnostics.CodeAnalysis;

namespace ByteShelfCommon
{
    /// <summary>
    /// The rules for shelf names, which appear in URLs: 1 to 63 characters of lowercase letters, digits,
    /// <c>-</c>, <c>_</c> and <c>.</c>, starting with a letter or digit.
    /// </summary>
    public static class ShelfName
    {
        /// <summary>The longest name a shelf can have.</summary>
        public const int MaxLength = 63;

        /// <summary>Whether <paramref name="name"/> can name a shelf, answering why not.</summary>
        public static bool TryValidate(string? name, [NotNullWhen(false)] out string? error)
        {
            if (string.IsNullOrEmpty(name) || name.Length > MaxLength)
            {
                error = $"A shelf name has 1 to {MaxLength} characters.";
                return false;
            }

            if (!IsLetterOrDigit(name[0]))
            {
                error = "A shelf name starts with a lowercase letter or a digit.";
                return false;
            }

            foreach (char character in name)
            {
                if (!IsLetterOrDigit(character) && character != '-' && character != '_' && character != '.')
                {
                    error = "A shelf name holds only lowercase letters, digits, '-', '_' and '.'.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>Throws unless <paramref name="name"/> passes <see cref="TryValidate"/>.</summary>
        /// <exception cref="ArgumentException">The name is not allowed.</exception>
        public static void Validate(string name)
        {
            if (!TryValidate(name, out string? error))
            {
                throw new ArgumentException($"'{name}' is not a shelf name: {error}", nameof(name));
            }
        }

        private static bool IsLetterOrDigit(char character)
        {
            return (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9');
        }
    }
}
