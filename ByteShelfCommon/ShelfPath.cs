using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace ByteShelfCommon
{
    /// <summary>
    /// The rules for paths inside a shelf, shared by the server and every client so both refuse the
    /// same input. A normalized path starts with <c>/</c>, separates names with <c>/</c>, has no
    /// trailing separator except the root itself (<c>/</c>), and holds every name in Unicode
    /// normalization form C, so a name typed on macOS and on Windows is the same name.
    /// </summary>
    /// <remarks>
    /// The rules keep names unambiguous on the shelf; they do not make every name a valid file name
    /// on every operating system. Windows, for one, refuses <c>CON</c>, <c>C:x</c> and a trailing dot,
    /// all of which a shelf allows, so a client that writes files to disk maps such names itself.
    /// </remarks>
    public static class ShelfPath
    {
        /// <summary>The separator between names.</summary>
        public const char Separator = '/';

        /// <summary>The root directory of a shelf.</summary>
        public const string Root = "/";

        /// <summary>The longest name a file or directory can have, in characters.</summary>
        public const int MaxNameLength = 255;

        /// <summary>The longest path, in characters.</summary>
        public const int MaxPathLength = 4096;

        /// <summary>
        /// Normalizes <paramref name="path"/>: a missing leading separator is added and one trailing
        /// separator is dropped. Every name in it must pass <see cref="TryValidateName"/>.
        /// </summary>
        /// <exception cref="ArgumentException">The path is empty or one of its names is not allowed.</exception>
        public static string Normalize(string path)
        {
            if (!TryNormalize(path, out string? normalized, out string? error))
            {
                throw new ArgumentException(error, nameof(path));
            }

            return normalized;
        }

        /// <summary>Normalizes <paramref name="path"/>, answering why it is refused instead of throwing.</summary>
        public static bool TryNormalize(string? path, [NotNullWhen(true)] out string? normalized, [NotNullWhen(false)] out string? error)
        {
            normalized = null;

            if (string.IsNullOrEmpty(path))
            {
                error = "A path is required.";
                return false;
            }

            if (path.Length > MaxPathLength)
            {
                error = $"A path can be at most {MaxPathLength} characters long.";
                return false;
            }

            string trimmed = path;
            if (trimmed.Length > 1 && trimmed[^1] == Separator && trimmed[^2] != Separator)
            {
                trimmed = trimmed[..^1];
            }

            if (trimmed == Root)
            {
                normalized = Root;
                error = null;
                return true;
            }

            if (trimmed[0] == Separator)
            {
                trimmed = trimmed[1..];
            }

            string[] names = trimmed.Split(Separator);
            for (int i = 0; i < names.Length; i++)
            {
                if (!TryValidateName(names[i], out error))
                {
                    error = $"The path '{path}' is not allowed: {error}";
                    return false;
                }

                names[i] = names[i].Normalize(NormalizationForm.FormC);
            }

            normalized = Root + string.Join(Separator, names);
            error = null;
            return true;
        }

        /// <summary>
        /// Whether <paramref name="name"/> can name a file or directory: not empty, not <c>.</c> or
        /// <c>..</c>, at most <see cref="MaxNameLength"/> characters, well-formed UTF-16, and free of
        /// separators, backslashes, control characters, and invisible formatting characters such as
        /// zero-width spaces and direction overrides, which would let two names look alike.
        /// </summary>
        public static bool TryValidateName(string? name, [NotNullWhen(false)] out string? error)
        {
            if (string.IsNullOrEmpty(name))
            {
                error = "a name cannot be empty.";
                return false;
            }

            if (name == "." || name == "..")
            {
                error = "'.' and '..' are not names.";
                return false;
            }

            if (name.Length > MaxNameLength)
            {
                error = $"a name can be at most {MaxNameLength} characters long.";
                return false;
            }

            int index = 0;
            while (index < name.Length)
            {
                // A lone surrogate would be stored as U+FFFD, so two different names could become one.
                if (Rune.DecodeFromUtf16(name.AsSpan(index), out Rune rune, out int consumed) != OperationStatus.Done)
                {
                    error = "a name must be well-formed Unicode text.";
                    return false;
                }

                if (rune.Value == Separator || rune.Value == '\\' || Rune.IsControl(rune) || IsInvisible(rune))
                {
                    error = "a name cannot contain '/', '\\', control characters or invisible formatting characters.";
                    return false;
                }

                index += consumed;
            }

            error = null;
            return true;
        }

        /// <summary>Format characters (zero-width joiners, direction overrides) and line or paragraph separators.</summary>
        private static bool IsInvisible(Rune rune)
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            return category == UnicodeCategory.Format
                || category == UnicodeCategory.LineSeparator
                || category == UnicodeCategory.ParagraphSeparator;
        }

        /// <summary>Throws unless <paramref name="name"/> passes <see cref="TryValidateName"/>.</summary>
        /// <exception cref="ArgumentException">The name is not allowed.</exception>
        public static void ValidateName(string name)
        {
            if (!TryValidateName(name, out string? error))
            {
                throw new ArgumentException($"The name '{name}' is not allowed: {error}", nameof(name));
            }
        }

        /// <summary>The names in a normalized path, outermost first; empty for the root.</summary>
        public static IReadOnlyList<string> GetNames(string normalizedPath)
        {
            if (normalizedPath == Root)
            {
                return Array.Empty<string>();
            }

            return normalizedPath[1..].Split(Separator);
        }

        /// <summary>The path of the directory holding a normalized path; the root for a top-level entry.</summary>
        /// <exception cref="ArgumentException">The path is the root, which has no parent.</exception>
        public static string GetParent(string normalizedPath)
        {
            if (normalizedPath == Root)
            {
                throw new ArgumentException("The root directory has no parent.", nameof(normalizedPath));
            }

            int lastSeparator = normalizedPath.LastIndexOf(Separator);
            return lastSeparator == 0 ? Root : normalizedPath[..lastSeparator];
        }

        /// <summary>The last name in a normalized path.</summary>
        /// <exception cref="ArgumentException">The path is the root, which has no name.</exception>
        public static string GetName(string normalizedPath)
        {
            if (normalizedPath == Root)
            {
                throw new ArgumentException("The root directory has no name.", nameof(normalizedPath));
            }

            return normalizedPath[(normalizedPath.LastIndexOf(Separator) + 1)..];
        }

        /// <summary>The path of <paramref name="name"/> inside the normalized directory path <paramref name="directoryPath"/>.</summary>
        public static string Combine(string directoryPath, string name)
        {
            ValidateName(name);
            string normalizedName = name.Normalize(NormalizationForm.FormC);
            return directoryPath == Root ? Root + normalizedName : directoryPath + Separator + normalizedName;
        }
    }
}
