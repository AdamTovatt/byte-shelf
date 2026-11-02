using System.Text;
using ByteShelfCommon;

namespace ByteShelfClient.Encryption
{
    /// <summary>An encrypted directory's check: made from its password, and how a password is told right.</summary>
    public static class FolderPassword
    {
        /// <summary>
        /// The check to encrypt a directory with (<see cref="IShelfClient.EncryptDirectoryAsync"/>): base64 of
        /// an age file of <see cref="FileEncryption.CheckText"/> encrypted with <paramref name="password"/>.
        /// </summary>
        /// <param name="password">The directory's password.</param>
        /// <param name="cancellationToken">Stops the encryption.</param>
        /// <param name="workFactor">The scrypt work factor, as <see cref="AgeEncryption.EncryptAsync"/> takes it.</param>
        public static async Task<string> CreateCheckAsync(string password, CancellationToken cancellationToken = default, int workFactor = AgeEncryption.DefaultWorkFactor)
        {
            using MemoryStream check = new MemoryStream();
            await AgeEncryption.EncryptAsync(new MemoryStream(FileEncryption.CheckBytes()), check, password, cancellationToken, workFactor).ConfigureAwait(false);
            return Convert.ToBase64String(check.ToArray());
        }

        /// <summary>Whether <paramref name="password"/> is <paramref name="folder"/>'s.</summary>
        /// <exception cref="AgeException">The check is damaged: it is not an age file of the check text.</exception>
        public static async Task<bool> MatchesAsync(EncryptedFolder folder, string password, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(folder);
            using MemoryStream text = new MemoryStream();
            try
            {
                await AgeEncryption.DecryptAsync(new MemoryStream(Convert.FromBase64String(folder.Check)), text, password, cancellationToken).ConfigureAwait(false);
            }
            catch (AgeException exception) when (exception.Failure == AgeFailure.NoMatch)
            {
                return false;
            }

            if (Encoding.UTF8.GetString(text.ToArray()) != FileEncryption.CheckText)
            {
                throw new AgeException(AgeFailure.Payload, $"The check of the encrypted folder '{folder.Directory.Path}' does not hold its check text.");
            }

            return true;
        }
    }
}
