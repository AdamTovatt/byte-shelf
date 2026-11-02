using ByteShelfClient.Encryption;

namespace ByteShelf.Tools
{
    /// <summary>
    /// A password to encrypt with, at the scrypt work factor that sets what guessing it costs. The CLI
    /// gives every one age's default (<see cref="AgeEncryption.DefaultWorkFactor"/>); only tests make
    /// the CLI with a lower one, through its internal constructor, since an unoptimized test build
    /// takes seconds a file at the default.
    /// </summary>
    internal sealed class Encryptor
    {
        private readonly int _workFactor;

        public Encryptor(string password, int workFactor)
        {
            Password = password;
            _workFactor = workFactor;
        }

        public string Password { get; }

        /// <summary>Writes <paramref name="plaintext"/> to <paramref name="output"/> as an age file this password opens.</summary>
        public Task EncryptAsync(Stream plaintext, Stream output, CancellationToken cancellationToken)
        {
            return AgeEncryption.EncryptAsync(plaintext, output, Password, cancellationToken, _workFactor);
        }

        /// <summary>The check a directory encrypted with this password is marked with.</summary>
        public Task<string> CreateCheckAsync(CancellationToken cancellationToken)
        {
            return FolderPassword.CreateCheckAsync(Password, cancellationToken, _workFactor);
        }
    }
}
