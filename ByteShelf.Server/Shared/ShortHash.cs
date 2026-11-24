using System.Security.Cryptography;
using System.Text;

namespace ByteShelf.Server.Shared
{
    /// <summary>
    /// A hash of several parts in order, short enough for an address or an id. Each part is hashed on its
    /// own first, so where one ends and the next begins is never in doubt.
    /// </summary>
    public sealed class ShortHash : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public void Add(string part)
        {
            _hash.AppendData(SHA256.HashData(Encoding.UTF8.GetBytes(part)));
        }

        public void AddFile(string path)
        {
            using FileStream contents = File.OpenRead(path);
            _hash.AppendData(SHA256.HashData(contents));
        }

        /// <summary>The hash of every part added, as 32 hexadecimal digits.</summary>
        public string Finish()
        {
            return Convert.ToHexStringLower(_hash.GetHashAndReset())[..32];
        }

        public void Dispose()
        {
            _hash.Dispose();
        }
    }
}
