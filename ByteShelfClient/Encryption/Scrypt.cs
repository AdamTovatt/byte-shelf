using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace ByteShelfClient.Encryption
{
    /// <summary>
    /// scrypt from RFC 7914, the key derivation age's passphrase recipient uses. .NET has PBKDF2 but not
    /// scrypt; this is the RFC's algorithm as written, checked against its test vectors.
    /// </summary>
    internal static class Scrypt
    {
        private const int SalsaWords = 16;

        /// <summary>The <paramref name="length"/>-byte key scrypt derives with N = 2^<paramref name="logN"/> and the given r and p.</summary>
        public static byte[] DeriveKey(byte[] password, byte[] salt, int logN, int r, int p, int length)
        {
            int blockWords = 32 * r;

            // V holds N blocks; past what one array holds the work factor is refused, not overflowed.
            if (logN < 1 || logN > 30 || ((long)blockWords << logN) > Array.MaxLength)
            {
                throw new ArgumentOutOfRangeException(nameof(logN), logN, $"The work factor {logN} is out of range for r = {r}.");
            }

            byte[] b = Rfc2898DeriveBytes.Pbkdf2(password, salt, 1, HashAlgorithmName.SHA256, p * 128 * r);
            uint[] x = new uint[blockWords];
            uint[] scratch = new uint[blockWords];
            uint[] v = new uint[blockWords << logN];

            for (int i = 0; i < p; i++)
            {
                Span<byte> chunk = b.AsSpan(i * 128 * r, 128 * r);
                for (int k = 0; k < blockWords; k++)
                {
                    x[k] = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(k * 4, 4));
                }

                RoMix(x, v, scratch, r, 1 << logN);

                for (int k = 0; k < blockWords; k++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(chunk.Slice(k * 4, 4), x[k]);
                }
            }

            Array.Clear(v);
            return Rfc2898DeriveBytes.Pbkdf2(password, b, 1, HashAlgorithmName.SHA256, length);
        }

        private static void RoMix(uint[] x, uint[] v, uint[] scratch, int r, int n)
        {
            int blockWords = 32 * r;
            for (int i = 0; i < n; i++)
            {
                Array.Copy(x, 0, v, i * blockWords, blockWords);
                BlockMix(x, scratch, r);
            }

            for (int i = 0; i < n; i++)
            {
                // Integerify: the first word of the last 64-byte block, modulo n (a power of two).
                int j = (int)(x[(2 * r - 1) * SalsaWords] & (uint)(n - 1));
                int offset = j * blockWords;
                for (int k = 0; k < blockWords; k++)
                {
                    x[k] ^= v[offset + k];
                }

                BlockMix(x, scratch, r);
            }
        }

        /// <summary>scryptBlockMix: x becomes Y0, Y2, ..., Y1, Y3, ..., using <paramref name="y"/> as room.</summary>
        private static void BlockMix(uint[] x, uint[] y, int r)
        {
            Span<uint> t = stackalloc uint[SalsaWords];
            x.AsSpan((2 * r - 1) * SalsaWords, SalsaWords).CopyTo(t);

            for (int i = 0; i < 2 * r; i++)
            {
                Span<uint> block = x.AsSpan(i * SalsaWords, SalsaWords);
                for (int k = 0; k < SalsaWords; k++)
                {
                    t[k] ^= block[k];
                }

                Salsa208(t);
                int to = (i % 2 == 0 ? i / 2 : r + (i / 2)) * SalsaWords;
                t.CopyTo(y.AsSpan(to, SalsaWords));
            }

            Array.Copy(y, x, 32 * r);
        }

        /// <summary>Salsa20/8 on one 64-byte block, its sixteen words held in locals so they stay in registers.</summary>
        private static void Salsa208(Span<uint> b)
        {
            uint x0 = b[0], x1 = b[1], x2 = b[2], x3 = b[3], x4 = b[4], x5 = b[5], x6 = b[6], x7 = b[7];
            uint x8 = b[8], x9 = b[9], x10 = b[10], x11 = b[11], x12 = b[12], x13 = b[13], x14 = b[14], x15 = b[15];
            for (int i = 0; i < 8; i += 2)
            {
                x4 ^= BitOperations.RotateLeft(x0 + x12, 7);
                x8 ^= BitOperations.RotateLeft(x4 + x0, 9);
                x12 ^= BitOperations.RotateLeft(x8 + x4, 13);
                x0 ^= BitOperations.RotateLeft(x12 + x8, 18);
                x9 ^= BitOperations.RotateLeft(x5 + x1, 7);
                x13 ^= BitOperations.RotateLeft(x9 + x5, 9);
                x1 ^= BitOperations.RotateLeft(x13 + x9, 13);
                x5 ^= BitOperations.RotateLeft(x1 + x13, 18);
                x14 ^= BitOperations.RotateLeft(x10 + x6, 7);
                x2 ^= BitOperations.RotateLeft(x14 + x10, 9);
                x6 ^= BitOperations.RotateLeft(x2 + x14, 13);
                x10 ^= BitOperations.RotateLeft(x6 + x2, 18);
                x3 ^= BitOperations.RotateLeft(x15 + x11, 7);
                x7 ^= BitOperations.RotateLeft(x3 + x15, 9);
                x11 ^= BitOperations.RotateLeft(x7 + x3, 13);
                x15 ^= BitOperations.RotateLeft(x11 + x7, 18);

                x1 ^= BitOperations.RotateLeft(x0 + x3, 7);
                x2 ^= BitOperations.RotateLeft(x1 + x0, 9);
                x3 ^= BitOperations.RotateLeft(x2 + x1, 13);
                x0 ^= BitOperations.RotateLeft(x3 + x2, 18);
                x6 ^= BitOperations.RotateLeft(x5 + x4, 7);
                x7 ^= BitOperations.RotateLeft(x6 + x5, 9);
                x4 ^= BitOperations.RotateLeft(x7 + x6, 13);
                x5 ^= BitOperations.RotateLeft(x4 + x7, 18);
                x11 ^= BitOperations.RotateLeft(x10 + x9, 7);
                x8 ^= BitOperations.RotateLeft(x11 + x10, 9);
                x9 ^= BitOperations.RotateLeft(x8 + x11, 13);
                x10 ^= BitOperations.RotateLeft(x9 + x8, 18);
                x12 ^= BitOperations.RotateLeft(x15 + x14, 7);
                x13 ^= BitOperations.RotateLeft(x12 + x15, 9);
                x14 ^= BitOperations.RotateLeft(x13 + x12, 13);
                x15 ^= BitOperations.RotateLeft(x14 + x13, 18);
            }

            b[0] += x0;
            b[1] += x1;
            b[2] += x2;
            b[3] += x3;
            b[4] += x4;
            b[5] += x5;
            b[6] += x6;
            b[7] += x7;
            b[8] += x8;
            b[9] += x9;
            b[10] += x10;
            b[11] += x11;
            b[12] += x12;
            b[13] += x13;
            b[14] += x14;
            b[15] += x15;
        }
    }
}
