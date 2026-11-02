using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ByteShelfClient.Encryption;
using ByteShelfCommon;

namespace ByteShelfClient.Tests.Encryption
{
    public sealed class AgeEncryptionTests
    {
        private static readonly string VectorsPath = Path.Combine(AppContext.BaseDirectory, "Encryption", "AgeVectors");

        public static TheoryData<string> Vectors()
        {
            TheoryData<string> names = new TheoryData<string>();
            foreach (string path in Directory.GetFiles(VectorsPath).Order(StringComparer.Ordinal))
            {
                names.Add(Path.GetFileName(path));
            }

            return names;
        }

        [Theory]
        [MemberData(nameof(Vectors))]
        public async Task Decrypt_OfficialVector_DoesWhatItExpects(string name)
        {
            Vector vector = Vector.Read(Path.Combine(VectorsPath, name));
            using MemoryStream output = new MemoryStream();
            Task decrypting = vector.Passphrase != null
                ? AgeEncryption.DecryptAsync(new MemoryStream(vector.File), output, vector.Passphrase, CancellationToken.None)
                : AgeEncryption.DecryptAsync(new MemoryStream(vector.File), output, _ => vector.FileKey, CancellationToken.None);

            if (vector.Expect == "success")
            {
                await decrypting;
                Assert.Equal(vector.PayloadSha256, Convert.ToHexStringLower(SHA256.HashData(output.ToArray())));
                return;
            }

            AgeException exception = await Assert.ThrowsAsync<AgeException>(() => decrypting);
            AgeFailure expected = vector.Expect switch
            {
                "header failure" or "HMAC failure" => AgeFailure.Header,
                "no match" => AgeFailure.NoMatch,
                "payload failure" => AgeFailure.Payload,
                _ => throw new InvalidOperationException($"Unknown expectation '{vector.Expect}'."),
            };
            Assert.Equal(expected, exception.Failure);
        }

        /// <summary>78 is how many CCTV vectors were copied in; this catches the build no longer copying them all to the output.</summary>
        [Fact]
        public void Vectors_AreAllThere()
        {
            Assert.Equal(78, Directory.GetFiles(VectorsPath).Length);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(64 * 1024)]
        [InlineData((64 * 1024) + 1)]
        [InlineData(3 * 64 * 1024)]
        public async Task EncryptThenDecrypt_AnyLength_AnswersTheSameBytes(int length)
        {
            byte[] plaintext = RandomNumberGenerator.GetBytes(length);
            using MemoryStream encrypted = new MemoryStream();
            await AgeEncryption.EncryptAsync(new MemoryStream(plaintext), encrypted, "räksmörgås", CancellationToken.None, workFactor: 10);
            using MemoryStream decrypted = new MemoryStream();

            await AgeEncryption.DecryptAsync(new MemoryStream(encrypted.ToArray()), decrypted, "räksmörgås", CancellationToken.None);

            Assert.Equal(plaintext, decrypted.ToArray());
            Assert.StartsWith("age-encryption.org/v1\n-> scrypt ", Encoding.ASCII.GetString(encrypted.ToArray(), 0, 40), StringComparison.Ordinal);
        }

        /// <summary>Slow on purpose (a real work factor 18 scrypt, seconds in a Debug build): the one guard on the library's default.</summary>
        [Fact]
        public async Task Encrypt_ByDefault_UsesWorkFactor18()
        {
            using MemoryStream encrypted = new MemoryStream();

            await AgeEncryption.EncryptAsync(new MemoryStream(new byte[1]), encrypted, "pw", CancellationToken.None);

            Assert.EndsWith(" 18", Encoding.ASCII.GetString(encrypted.ToArray()).Split('\n')[1], StringComparison.Ordinal);
        }

        /// <summary>Slow on purpose, as above: every file this library and age write by default must open again, so the cap on what is decrypted is at least 18.</summary>
        [Fact]
        public async Task Decrypt_AFileAtTheDefaultWorkFactor_AnswersItsContents()
        {
            using MemoryStream encrypted = new MemoryStream();
            await AgeEncryption.EncryptAsync(new MemoryStream(Encoding.UTF8.GetBytes("at the default")), encrypted, "pw", CancellationToken.None, AgeEncryption.DefaultWorkFactor);
            using MemoryStream decrypted = new MemoryStream();

            await AgeEncryption.DecryptAsync(new MemoryStream(encrypted.ToArray()), decrypted, "pw", CancellationToken.None);

            Assert.Equal("at the default", Encoding.UTF8.GetString(decrypted.ToArray()));
        }

        /// <summary>
        /// typage.age is what typage 0.3.1 (age in JavaScript, which the web UI depends on) wrote for 100 KiB of (i % 251) with this
        /// password at work factor 10; byteshelf.client's interop test opens the file this library writes
        /// for the same bytes. Together they check the two read each other both ways.
        /// </summary>
        [Fact]
        public async Task Decrypt_AFileTypageEncrypted_AnswersItsContents()
        {
            byte[] expected = Enumerable.Range(0, 100 * 1024).Select(i => (byte)(i % 251)).ToArray();
            await using FileStream encrypted = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Encryption", "Interop", "typage.age"));
            using MemoryStream decrypted = new MemoryStream();

            await AgeEncryption.DecryptAsync(encrypted, decrypted, "correct horse ☃ räksmörgås", CancellationToken.None);

            Assert.Equal(expected, decrypted.ToArray());
        }

        [Fact]
        public async Task Decrypt_WrongPassword_IsNoMatchSayingSo()
        {
            using MemoryStream encrypted = new MemoryStream();
            await AgeEncryption.EncryptAsync(new MemoryStream(new byte[] { 1 }), encrypted, "right", CancellationToken.None, workFactor: 10);

            AgeException exception = await Assert.ThrowsAsync<AgeException>(
                () => AgeEncryption.DecryptAsync(new MemoryStream(encrypted.ToArray()), Stream.Null, "wrong", CancellationToken.None));

            Assert.Equal((AgeFailure.NoMatch, "The password is wrong."), (exception.Failure, exception.Message));
        }

        [Fact]
        public async Task IsOpenedBy_ItsPasswordAndAnother_AnswersTrueThenFalse()
        {
            byte[] encrypted = await EncryptAsync("right");

            Assert.True(await AgeEncryption.IsOpenedByAsync(new MemoryStream(encrypted), "right", CancellationToken.None));
            Assert.False(await AgeEncryption.IsOpenedByAsync(new MemoryStream(encrypted), "wrong", CancellationToken.None));
        }

        [Fact]
        public async Task IsOpenedBy_AChangedMac_IsAHeaderFailure()
        {
            byte[] encrypted = await EncryptAsync("right");
            int macAt = encrypted.AsSpan().IndexOf("\n--- "u8) + 5;
            encrypted[macAt] = encrypted[macAt] == (byte)'A' ? (byte)'B' : (byte)'A';

            AgeException exception = await Assert.ThrowsAsync<AgeException>(
                () => AgeEncryption.IsOpenedByAsync(new MemoryStream(encrypted), "right", CancellationToken.None));

            Assert.Equal(AgeFailure.Header, exception.Failure);
        }

        /// <summary>
        /// typage-check.txt is a folder check the web UI's age.ts makes (typage at work factor 10, of its
        /// CHECK_TEXT) for this password, so a check text that drifts from <see cref="FileEncryption.CheckText"/>
        /// fails here. byteshelf.client's interop test matches the check this library makes the same way.
        /// </summary>
        [Fact]
        public async Task FolderPasswordMatches_ACheckTypageMade_AnswersTrueForItsPasswordOnly()
        {
            EncryptedFolder folder = new EncryptedFolder
            {
                Directory = new ShelfDirectory { Id = Guid.NewGuid(), ShelfId = Guid.NewGuid(), Path = "/vault", Name = "vault", CreatedAt = DateTimeOffset.UnixEpoch, Encrypted = true },
                Check = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Encryption", "Interop", "typage-check.txt")).Trim(),
            };

            Assert.True(await FolderPassword.MatchesAsync(folder, "correct horse ☃ räksmörgås"));
            Assert.False(await FolderPassword.MatchesAsync(folder, "another password"));
        }

        [Fact]
        public async Task FolderPasswordMatches_ACheckOfOtherText_IsAPayloadFailure()
        {
            using MemoryStream check = new MemoryStream();
            await AgeEncryption.EncryptAsync(new MemoryStream("not the check text"u8.ToArray()), check, "right", CancellationToken.None, workFactor: 10);
            EncryptedFolder folder = new EncryptedFolder
            {
                Directory = new ShelfDirectory { Id = Guid.NewGuid(), ShelfId = Guid.NewGuid(), Path = "/vault", Name = "vault", CreatedAt = DateTimeOffset.UnixEpoch, Encrypted = true },
                Check = Convert.ToBase64String(check.ToArray()),
            };

            AgeException exception = await Assert.ThrowsAsync<AgeException>(() => FolderPassword.MatchesAsync(folder, "right"));

            Assert.Equal(AgeFailure.Payload, exception.Failure);
        }

        [Fact]
        public void Scrypt_AWorkFactorPastWhatAnArrayHolds_IsRefusedNotOverflowed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Scrypt.DeriveKey(new byte[1], new byte[1], 23, 8, 1, 32));
        }

        [Theory]
        [InlineData("", "", 4, 1, 1, "77d6576238657b203b19ca42c18a0497f16b4844e3074ae8dfdffa3fede21442fcd0069ded0948f8326a753a0fc81f17e8d3e0fb2e0d3628cf35e20c38d18906")]
        [InlineData("password", "NaCl", 10, 8, 16, "fdbabe1c9d3472007856e7190d01e9fe7c6ad7cbc8237830e77376634b3731622eaf30d92e22a3886ff109279d9830dac727afb94a83ee6d8360cbdfa2cc0640")]
        public void Scrypt_Rfc7914Vectors_DeriveTheirKeys(string password, string salt, int logN, int r, int p, string expected)
        {
            byte[] key = Scrypt.DeriveKey(Encoding.ASCII.GetBytes(password), Encoding.ASCII.GetBytes(salt), logN, r, p, 64);

            Assert.Equal(expected, Convert.ToHexStringLower(key));
        }

        private static async Task<byte[]> EncryptAsync(string password)
        {
            using MemoryStream encrypted = new MemoryStream();
            await AgeEncryption.EncryptAsync(new MemoryStream(new byte[] { 1 }), encrypted, password, CancellationToken.None, workFactor: 10);
            return encrypted.ToArray();
        }

        /// <summary>A CCTV vector: <c>key: value</c> lines, a blank line, then the age file (zlib-compressed when it says so).</summary>
        private sealed record Vector(string Expect, string? PayloadSha256, byte[] FileKey, string? Passphrase, byte[] File)
        {
            public static Vector Read(string path)
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                int split = bytes.AsSpan().IndexOf("\n\n"u8);
                Dictionary<string, string> fields = Encoding.UTF8.GetString(bytes, 0, split)
                    .Split('\n')
                    .Select(line => line.Split(": ", 2))
                    .GroupBy(parts => parts[0])
                    .ToDictionary(group => group.Key, group => group.First()[1]);

                byte[] file = bytes[(split + 2)..];
                if (fields.GetValueOrDefault("compressed") == "zlib")
                {
                    using ZLibStream zlib = new ZLibStream(new MemoryStream(file), CompressionMode.Decompress);
                    using MemoryStream inflated = new MemoryStream();
                    zlib.CopyTo(inflated);
                    file = inflated.ToArray();
                }

                return new Vector(fields["expect"], fields.GetValueOrDefault("payload"), Convert.FromHexString(fields["file key"]), fields.GetValueOrDefault("passphrase"), file);
            }
        }
    }
}
