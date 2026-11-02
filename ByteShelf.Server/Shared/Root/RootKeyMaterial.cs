using System.Security.Cryptography;
using ByteShelf.Server.Shared.IO;

namespace ByteShelf.Server.Shared.Root
{
    /// <summary>
    /// Secrets the server generates for itself and keeps in the root's <c>keys/</c> folder, so the root
    /// carries them along with the data: a moved or restored root keeps signing in the users it had.
    /// </summary>
    public static class RootKeyMaterial
    {
        public const string JwtSecretFileName = "jwt-secret";

        /// <summary>
        /// Answers the secret stored as <paramref name="fileName"/> under <c>keys/</c>, generating
        /// <paramref name="byteCount"/> random bytes and writing them the first time. The folder and the
        /// file are readable by the owner only from the moment they exist, and the file is written under
        /// a temporary name and renamed, so a crash never leaves a short secret.
        /// </summary>
        public static string GetOrCreate(RootFolder root, string fileName, int byteCount = 64)
        {
            OwnerOnly.RestrictDirectory(root.KeysPath);

            string path = Path.Combine(root.KeysPath, fileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path).Trim();
            }

            string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount));
            OwnerOnly.WriteThenRename(path, secret);
            return secret;
        }
    }
}
