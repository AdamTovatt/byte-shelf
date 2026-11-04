using ByteShelfClient.Encryption;
using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>What a download does with an encrypted file: saves it as it is stored, decrypts it, or leaves it out.</summary>
    internal sealed class EncryptedFiles
    {
        /// <summary>What an encrypted file saved as it is stored ends with.</summary>
        public const string AgeExtension = ".age";

        /// <summary>Saved as the age file it is, under its name with <see cref="AgeExtension"/> added.</summary>
        public static readonly EncryptedFiles SavedAsTheyAre = new EncryptedFiles(null, null);

        private readonly string? _password;
        private readonly string? _leaveOutBecause;

        private EncryptedFiles(string? password, string? leaveOutBecause)
        {
            _password = password;
            _leaveOutBecause = leaveOutBecause;
        }

        public static EncryptedFiles DecryptedWith(string password)
        {
            return new EncryptedFiles(password, null);
        }

        public static EncryptedFiles LeftOut(string because)
        {
            return new EncryptedFiles(null, because);
        }

        /// <summary>Whether <paramref name="file"/> is saved as the age file it is stored as.</summary>
        public bool KeepsEncrypted(ShelfFile file)
        {
            return file.Encrypted && _password == null && _leaveOutBecause == null;
        }

        /// <summary>
        /// The local name for <paramref name="file"/> when the download chooses it from
        /// <paramref name="name"/>: one kept encrypted gets the age file's extension, so nothing opens it
        /// as what it was.
        /// </summary>
        public string NameFor(ShelfFile file, string name)
        {
            return KeepsEncrypted(file) ? name + AgeExtension : name;
        }

        /// <summary>Writes <paramref name="content"/>, the stored bytes of <paramref name="file"/>, to <paramref name="output"/> as this says.</summary>
        /// <exception cref="UsageException">An encrypted file is to be left out.</exception>
        /// <exception cref="AgeException">The password does not open the file, or it is damaged.</exception>
        public Task WriteAsync(ShelfFile file, Stream content, Stream output, CancellationToken cancellationToken)
        {
            if (!file.Encrypted)
            {
                return content.CopyToAsync(output, cancellationToken);
            }

            if (_leaveOutBecause != null)
            {
                throw new UsageException(_leaveOutBecause);
            }

            return _password == null
                ? content.CopyToAsync(output, cancellationToken)
                : AgeEncryption.DecryptAsync(content, output, _password, cancellationToken);
        }
    }
}
