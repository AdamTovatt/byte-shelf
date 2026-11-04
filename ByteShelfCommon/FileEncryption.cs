using System.Text;

namespace ByteShelfCommon
{
    /// <summary>
    /// Password encryption, as every part of ByteShelf sees it. Clients encrypt a file's contents
    /// themselves, as an age file (c2sp.org/age) with a password, so <c>age -d</c> opens them anywhere;
    /// the password and the plaintext never reach the server. Names, sizes, directories and descriptions
    /// stay as they are, so search keeps finding encrypted files.
    /// </summary>
    public static class FileEncryption
    {
        /// <summary>How every age file starts: its version line.</summary>
        public static ReadOnlySpan<byte> AgeHeader => "age-encryption.org/v1\n"u8;

        /// <summary>
        /// The text an encrypted directory's check value holds, encrypted with the directory's password:
        /// a client that decrypts it to this has the right password.
        /// </summary>
        public const string CheckText = "ByteShelf encrypted folder";

        /// <summary>The most bytes a check may have: it is a short age file, and anything this long is not one.</summary>
        public const int MaxCheckBytes = 1024;

        /// <summary>Whether <paramref name="contents"/> start as an age file.</summary>
        public static bool StartsAsAgeFile(ReadOnlySpan<byte> contents)
        {
            return contents.StartsWith(AgeHeader);
        }

        /// <summary>The bytes of a check given as base64, as a directory is encrypted with it.</summary>
        /// <exception cref="ArgumentException">It is not base64 of an age file of at most <see cref="MaxCheckBytes"/> bytes.</exception>
        public static byte[] DecodeCheck(string check)
        {
            ArgumentNullException.ThrowIfNull(check);
            byte[] bytes;
            try
            {
                // Measured before decoding, so an oversized one is never decoded.
                bytes = check.Length <= ((MaxCheckBytes + 2) / 3 * 4) ? Convert.FromBase64String(check) : Array.Empty<byte>();
            }
            catch (FormatException)
            {
                throw new ArgumentException("An encrypted folder's check must be base64.", nameof(check));
            }

            if (bytes.Length == 0 || bytes.Length > MaxCheckBytes || !StartsAsAgeFile(bytes))
            {
                throw new ArgumentException($"An encrypted folder's check must be an age file of its check text, encrypted with its password, of at most {MaxCheckBytes} bytes.", nameof(check));
            }

            return bytes;
        }

        /// <summary>The check text's UTF-8 bytes.</summary>
        public static byte[] CheckBytes()
        {
            return Encoding.UTF8.GetBytes(CheckText);
        }
    }
}
