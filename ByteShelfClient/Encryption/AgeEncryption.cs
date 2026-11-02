using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ByteShelfCommon;

namespace ByteShelfClient.Encryption
{
    /// <summary>A recipient stanza of an age header: its arguments and its decoded body.</summary>
    internal sealed record AgeStanza(IReadOnlyList<string> Arguments, byte[] Body);

    /// <summary>
    /// age v1 (c2sp.org/age) with its passphrase (scrypt) recipient: the format ByteShelf's encrypted
    /// files are stored in, so <c>age -d</c> opens them anywhere and typage (age in JavaScript) reads
    /// what this writes. Implemented from the specification and checked against its official test
    /// vectors (C2SP CCTV) and typage's output. It needs ChaCha20-Poly1305 from the platform
    /// (<see cref="IsSupported"/>).
    /// </summary>
    public static partial class AgeEncryption
    {
        /// <summary>The scrypt work factor (log2 N) new files are encrypted with, age's and typage's default.</summary>
        public const int DefaultWorkFactor = 18;

        /// <summary>The highest work factor a file is decrypted with, as age allows; past it the file is refused.</summary>
        /// <remarks>
        /// Decrypting at it takes 4 GiB of memory, which a file anyone may store can ask of whoever opens
        /// it. Kept at age's own cap anyway, so every file age writes opens here too; a smaller machine
        /// fails that one file, and an encrypted folder's files are checked against its password first.
        /// </remarks>
        public const int MaxWorkFactor = 22;

        /// <summary>The header's first line, without its line feed: <see cref="FileEncryption.AgeHeader"/>, which the server checks for.</summary>
        private static readonly string VersionLine = Encoding.ASCII.GetString(FileEncryption.AgeHeader[..^1]);
        private const string ScryptLabel = "age-encryption.org/v1/scrypt";
        private const int FileKeyBytes = 16;
        private const int SaltBytes = 16;
        private const int KeyBytes = 32;
        private const int MacBytes = 32;
        private const int ScryptBlockSize = 8;
        private const int ScryptParallelism = 1;
        private const int AeadNonceBytes = 12;
        private const int NonceBytes = 16;
        private const int ChunkBytes = 64 * 1024;
        private const int TagBytes = 16;
        private const int MaxHeaderBytes = 64 * 1024;
        private const int ColumnsPerLine = 64;
        private const string MacLineStart = "--- ";
        private const int MacBase64Chars = 43;

        /// <summary>Whether this platform has the ChaCha20-Poly1305 that encrypting and decrypting need.</summary>
        public static bool IsSupported => ChaCha20Poly1305.IsSupported;

        /// <summary>Encrypts <paramref name="plaintext"/> to <paramref name="output"/> as an age file opened by <paramref name="passphrase"/>.</summary>
        /// <param name="plaintext">What to encrypt, read to its end.</param>
        /// <param name="output">Where the age file is written.</param>
        /// <param name="passphrase">The password, used as its UTF-8 bytes, as age and typage use it.</param>
        /// <param name="cancellationToken">Stops the encryption.</param>
        /// <param name="workFactor">The scrypt work factor (log2 N); tests pass a small one to run fast.</param>
        /// <exception cref="PlatformNotSupportedException">The platform has no ChaCha20-Poly1305.</exception>
        public static async Task EncryptAsync(Stream plaintext, Stream output, string passphrase, CancellationToken cancellationToken, int workFactor = DefaultWorkFactor)
        {
            RequireSupport();
            byte[] fileKey = RandomNumberGenerator.GetBytes(FileKeyBytes);
            byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
            byte[] body = Seal(WrapKey(passphrase, salt, workFactor), new byte[AeadNonceBytes], fileKey);

            StringBuilder header = new StringBuilder()
                .Append(VersionLine).Append('\n')
                .Append("-> scrypt ").Append(Base64(salt)).Append(' ').Append(workFactor.ToString(CultureInfo.InvariantCulture)).Append('\n');
            AppendWrapped(header, Base64(body));
            header.Append(MacLineStart.TrimEnd());

            byte[] macInput = Encoding.ASCII.GetBytes(header.ToString());
            byte[] mac = HMACSHA256.HashData(HeaderKey(fileKey), macInput);
            await output.WriteAsync(macInput, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(Encoding.ASCII.GetBytes($" {Base64(mac)}\n"), cancellationToken).ConfigureAwait(false);

            byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            await output.WriteAsync(nonce, cancellationToken).ConfigureAwait(false);
            using ChaCha20Poly1305 aead = new ChaCha20Poly1305(PayloadKey(fileKey, nonce));

            // One byte past a chunk tells whether more follow, so the last chunk is known before it is sealed.
            byte[] buffer = new byte[ChunkBytes + 1];
            byte[] sealedChunk = new byte[ChunkBytes + TagBytes];
            int held = 0;
            long counter = 0;
            while (true)
            {
                held += await ReadFullyAsync(plaintext, buffer.AsMemory(held), cancellationToken).ConfigureAwait(false);
                bool last = held <= ChunkBytes;
                int length = Math.Min(held, ChunkBytes);

                aead.Encrypt(ChunkNonce(counter, last), buffer.AsSpan(0, length), sealedChunk.AsSpan(0, length), sealedChunk.AsSpan(length, TagBytes));
                await output.WriteAsync(sealedChunk.AsMemory(0, length + TagBytes), cancellationToken).ConfigureAwait(false);
                if (last)
                {
                    return;
                }

                buffer[0] = buffer[ChunkBytes];
                held = 1;
                counter++;
            }
        }

        /// <summary>
        /// Decrypts the age file <paramref name="ciphertext"/> opened by <paramref name="passphrase"/> to
        /// <paramref name="output"/>, a chunk at a time: when the file turns out damaged part way, what came
        /// before is already written, so write to a side file and keep it only once this returns.
        /// </summary>
        /// <param name="ciphertext">The age file, read to its end.</param>
        /// <param name="output">Where the contents are written.</param>
        /// <param name="passphrase">The password it was encrypted with.</param>
        /// <param name="cancellationToken">Stops the decryption.</param>
        /// <exception cref="AgeException">It is not an age file, the password does not open it, or it is damaged.</exception>
        /// <exception cref="PlatformNotSupportedException">The platform has no ChaCha20-Poly1305.</exception>
        public static Task DecryptAsync(Stream ciphertext, Stream output, string passphrase, CancellationToken cancellationToken)
        {
            return DecryptAsync(ciphertext, output, stanzas => UnwrapWithPassphrase(stanzas, passphrase), cancellationToken);
        }

        /// <summary>
        /// Whether <paramref name="passphrase"/> opens the age file <paramref name="ciphertext"/>, reading
        /// only its header: the password unwraps its file key and the header's MAC holds.
        /// </summary>
        /// <exception cref="AgeException">It is not an age file, or not one encrypted with a password.</exception>
        /// <exception cref="PlatformNotSupportedException">The platform has no ChaCha20-Poly1305.</exception>
        public static async Task<bool> IsOpenedByAsync(Stream ciphertext, string passphrase, CancellationToken cancellationToken)
        {
            RequireSupport();
            (IReadOnlyList<AgeStanza> stanzas, byte[] macInput, byte[] mac) = await ReadHeaderAsync(new AgeInput(ciphertext), cancellationToken).ConfigureAwait(false);
            byte[] fileKey;
            try
            {
                fileKey = UnwrapWithPassphrase(stanzas, passphrase);
            }
            catch (AgeException exception) when (exception.Failure == AgeFailure.NoMatch && stanzas.Any(stanza => stanza.Arguments[0] == "scrypt"))
            {
                return false;
            }

            VerifyHeaderMac(fileKey, macInput, mac);
            return true;
        }

        /// <summary>Decrypts with the file key <paramref name="unwrap"/> answers from the header's stanzas.</summary>
        internal static async Task DecryptAsync(Stream ciphertext, Stream output, Func<IReadOnlyList<AgeStanza>, byte[]> unwrap, CancellationToken cancellationToken)
        {
            RequireSupport();
            AgeInput input = new AgeInput(ciphertext);
            (IReadOnlyList<AgeStanza> stanzas, byte[] macInput, byte[] mac) = await ReadHeaderAsync(input, cancellationToken).ConfigureAwait(false);
            byte[] fileKey = unwrap(stanzas);
            VerifyHeaderMac(fileKey, macInput, mac);

            byte[] nonce = new byte[NonceBytes];
            // Part of the header as age counts it: a file cut off here has no contents to be damaged.
            if (await input.ReadFullyAsync(nonce, cancellationToken).ConfigureAwait(false) != NonceBytes)
            {
                throw HeaderFailure("it ends before its contents begin");
            }

            using ChaCha20Poly1305 aead = new ChaCha20Poly1305(PayloadKey(fileKey, nonce));
            byte[] buffer = new byte[ChunkBytes + TagBytes + 1];
            byte[] plain = new byte[ChunkBytes];
            int held = 0;
            long counter = 0;
            while (true)
            {
                held += await input.ReadFullyAsync(buffer.AsMemory(held), cancellationToken).ConfigureAwait(false);
                bool last = held <= ChunkBytes + TagBytes;
                int length = Math.Min(held, ChunkBytes + TagBytes);
                if (length < TagBytes || (last && length == TagBytes && counter > 0))
                {
                    throw Damaged(length == 0 ? "it has no contents at all" : "a part of it is cut short");
                }

                int plainLength = length - TagBytes;
                try
                {
                    aead.Decrypt(ChunkNonce(counter, last), buffer.AsSpan(0, plainLength), buffer.AsSpan(plainLength, TagBytes), plain.AsSpan(0, plainLength));
                }
                catch (AuthenticationTagMismatchException)
                {
                    throw Damaged(last ? "its end is missing or changed" : "a part of it was changed");
                }

                await output.WriteAsync(plain.AsMemory(0, plainLength), cancellationToken).ConfigureAwait(false);
                if (last)
                {
                    return;
                }

                buffer[0] = buffer[ChunkBytes + TagBytes];
                held = 1;
                counter++;
            }
        }

        /// <summary>The file key a passphrase opens: the header must hold exactly one scrypt stanza.</summary>
        private static byte[] UnwrapWithPassphrase(IReadOnlyList<AgeStanza> stanzas, string passphrase)
        {
            AgeStanza? stanza = stanzas.FirstOrDefault(candidate => candidate.Arguments[0] == "scrypt");
            if (stanza == null)
            {
                throw new AgeException(AgeFailure.NoMatch, "The file is not encrypted with a password.");
            }

            if (stanzas.Count != 1)
            {
                throw HeaderFailure("a password stanza must be the header's only one");
            }

            if (stanza.Arguments.Count != 3 || !TryDecodeBase64(stanza.Arguments[1], out byte[]? salt) || salt.Length != SaltBytes)
            {
                throw HeaderFailure("its password stanza is malformed");
            }

            if (!WorkFactorPattern().IsMatch(stanza.Arguments[2])
                || !int.TryParse(stanza.Arguments[2], NumberStyles.None, CultureInfo.InvariantCulture, out int workFactor)
                || workFactor > MaxWorkFactor)
            {
                throw HeaderFailure($"its password's work factor is not a number from 1 to {MaxWorkFactor}");
            }

            if (stanza.Body.Length != FileKeyBytes + TagBytes)
            {
                throw HeaderFailure("its password stanza is malformed");
            }

            try
            {
                return Open(WrapKey(passphrase, salt, workFactor), new byte[AeadNonceBytes], stanza.Body);
            }
            catch (AuthenticationTagMismatchException)
            {
                throw new AgeException(AgeFailure.NoMatch, "The password is wrong.");
            }
        }

        /// <summary>The key a password wraps a file key with: scrypt of it with the file's salt, at its work factor.</summary>
        /// <exception cref="AgeException">The work factor needs more memory than this process could have.</exception>
        /// <remarks>
        /// Not cancellable part way: at <see cref="MaxWorkFactor"/> it takes seconds.
        /// </remarks>
        private static byte[] WrapKey(string passphrase, byte[] salt, int workFactor)
        {
            try
            {
                return Scrypt.DeriveKey(Encoding.UTF8.GetBytes(passphrase), [.. Encoding.ASCII.GetBytes(ScryptLabel), .. salt], workFactor, ScryptBlockSize, ScryptParallelism, KeyBytes);
            }
            catch (OutOfMemoryException)
            {
                // The one allocation that grows with the work factor is scrypt's own: failing it leaves the
                // process as it was, so this file fails and the rest go on.
                long mebibytes = ((long)128 * ScryptBlockSize << workFactor) / (1024 * 1024);
                throw new AgeException(AgeFailure.OutOfMemory, $"Its password's work factor {workFactor} needs {mebibytes} MiB of memory, more than could be had.");
            }
        }

        /// <exception cref="AgeException">The header's MAC is not the one <paramref name="fileKey"/> gives it.</exception>
        private static void VerifyHeaderMac(byte[] fileKey, byte[] macInput, byte[] mac)
        {
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(HeaderKey(fileKey), macInput), mac))
            {
                throw new AgeException(AgeFailure.Header, "The file's header does not match its MAC: it was changed or damaged.");
            }
        }

        private static async Task<(IReadOnlyList<AgeStanza> Stanzas, byte[] MacInput, byte[] Mac)> ReadHeaderAsync(AgeInput input, CancellationToken cancellationToken)
        {
            MemoryStream header = new MemoryStream();
            if (await ReadLineAsync(input, header, cancellationToken).ConfigureAwait(false) != VersionLine)
            {
                throw HeaderFailure("it does not start as an age v1 file");
            }

            List<AgeStanza> stanzas = new List<AgeStanza>();
            while (true)
            {
                long lineStart = header.Length;
                string line = await ReadLineAsync(input, header, cancellationToken).ConfigureAwait(false);
                if (line.StartsWith(MacLineStart.TrimEnd(), StringComparison.Ordinal))
                {
                    if (stanzas.Count == 0
                        || line.Length != MacLineStart.Length + MacBase64Chars
                        || !line.StartsWith(MacLineStart, StringComparison.Ordinal)
                        || !TryDecodeBase64(line[MacLineStart.Length..], out byte[]? mac)
                        || mac.Length != MacBytes)
                    {
                        throw HeaderFailure("its MAC line is malformed");
                    }

                    // What the MAC covers: the header up to and including "---", without the space after it.
                    return (stanzas, header.GetBuffer().AsSpan(0, (int)lineStart + MacLineStart.Length - 1).ToArray(), mac);
                }

                if (!line.StartsWith("-> ", StringComparison.Ordinal))
                {
                    throw HeaderFailure("a line of its header is neither a stanza nor its MAC");
                }

                string[] arguments = line[3..].Split(' ');
                if (arguments.Any(argument => argument.Length == 0 || argument.Any(character => character < 0x21 || character > 0x7E)))
                {
                    throw HeaderFailure("a stanza has an empty or invalid argument");
                }

                StringBuilder body = new StringBuilder();
                while (true)
                {
                    string bodyLine = await ReadLineAsync(input, header, cancellationToken).ConfigureAwait(false);
                    if (bodyLine.Length > ColumnsPerLine)
                    {
                        throw HeaderFailure("a stanza's body line is too long");
                    }

                    body.Append(bodyLine);
                    if (bodyLine.Length < ColumnsPerLine)
                    {
                        break;
                    }
                }

                if (!TryDecodeBase64(body.ToString(), out byte[]? decoded))
                {
                    throw HeaderFailure("a stanza's body is not canonical base64");
                }

                stanzas.Add(new AgeStanza(arguments, decoded));
            }
        }

        /// <summary>A header line without its line feed, kept in <paramref name="header"/> with it.</summary>
        private static async Task<string> ReadLineAsync(AgeInput input, MemoryStream header, CancellationToken cancellationToken)
        {
            long start = header.Length;
            while (true)
            {
                int value = await input.ReadByteAsync(cancellationToken).ConfigureAwait(false);
                if (value < 0)
                {
                    throw HeaderFailure("its header ends early");
                }

                header.WriteByte((byte)value);
                if (header.Length > MaxHeaderBytes)
                {
                    throw HeaderFailure("its header is too long");
                }

                if (value == '\n')
                {
                    return LineText(header.GetBuffer().AsSpan((int)start, (int)(header.Length - start - 1)));
                }
            }
        }

        private static string LineText(ReadOnlySpan<byte> line)
        {
            foreach (byte character in line)
            {
                if (character < 0x20 || character > 0x7E)
                {
                    throw HeaderFailure("its header holds a character it may not");
                }
            }

            return Encoding.ASCII.GetString(line);
        }

        private static byte[] Seal(byte[] key, byte[] nonce, byte[] plaintext)
        {
            using ChaCha20Poly1305 aead = new ChaCha20Poly1305(key);
            byte[] result = new byte[plaintext.Length + TagBytes];
            aead.Encrypt(nonce, plaintext, result.AsSpan(0, plaintext.Length), result.AsSpan(plaintext.Length));
            return result;
        }

        /// <exception cref="AuthenticationTagMismatchException"><paramref name="key"/> did not seal <paramref name="sealedBytes"/>, or they were changed.</exception>
        private static byte[] Open(byte[] key, byte[] nonce, byte[] sealedBytes)
        {
            using ChaCha20Poly1305 aead = new ChaCha20Poly1305(key);
            byte[] result = new byte[sealedBytes.Length - TagBytes];
            aead.Decrypt(nonce, sealedBytes.AsSpan(0, result.Length), sealedBytes.AsSpan(result.Length), result);
            return result;
        }

        private static byte[] HeaderKey(byte[] fileKey)
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, fileKey, KeyBytes, salt: Array.Empty<byte>(), info: Encoding.ASCII.GetBytes("header"));
        }

        private static byte[] PayloadKey(byte[] fileKey, byte[] nonce)
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, fileKey, KeyBytes, salt: nonce, info: Encoding.ASCII.GetBytes("payload"));
        }

        /// <summary>The 11-byte big-endian chunk counter, then 1 for the last chunk and 0 before it.</summary>
        private static byte[] ChunkNonce(long counter, bool last)
        {
            byte[] nonce = new byte[AeadNonceBytes];
            BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(3, 8), counter);
            nonce[11] = last ? (byte)1 : (byte)0;
            return nonce;
        }

        private static void AppendWrapped(StringBuilder header, string base64)
        {
            int at = 0;
            do
            {
                int length = Math.Min(ColumnsPerLine, base64.Length - at);
                header.Append(base64, at, length).Append('\n');
                at += length;

                // A body whose last line is full ends with an empty line.
                if (length == ColumnsPerLine && at == base64.Length)
                {
                    header.Append('\n');
                }
            }
            while (at < base64.Length);
        }

        private static string Base64(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=');
        }

        /// <summary>Decodes unpadded base64, refusing any encoding that is not the canonical one.</summary>
        private static bool TryDecodeBase64(string text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? bytes)
        {
            bytes = null;
            if (text.Length % 4 == 1 || text.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '+' || character == '/')))
            {
                return false;
            }

            try
            {
                byte[] decoded = Convert.FromBase64String(text + new string('=', (4 - (text.Length % 4)) % 4));
                if (Base64(decoded) != text)
                {
                    return false;
                }

                bytes = decoded;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static async Task<int> ReadFullyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            return await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        }

        private static void RequireSupport()
        {
            if (!IsSupported)
            {
                throw new PlatformNotSupportedException("Encrypted files need ChaCha20-Poly1305, which this platform's cryptography does not offer.");
            }
        }

        private static AgeException HeaderFailure(string why)
        {
            return new AgeException(AgeFailure.Header, $"The file is not a valid age file: {why}.");
        }

        private static AgeException Damaged(string why)
        {
            return new AgeException(AgeFailure.Payload, $"The encrypted file is damaged: {why}.");
        }

        [GeneratedRegex("^[1-9][0-9]*$")]
        private static partial Regex WorkFactorPattern();

        /// <summary>A stream read a byte at a time through the header, then in blocks, without reading past what is asked.</summary>
        private sealed class AgeInput
        {
            private readonly Stream _stream;
            private readonly byte[] _buffer = new byte[4096];
            private int _position;
            private int _length;

            public AgeInput(Stream stream)
            {
                _stream = stream;
            }

            public async ValueTask<int> ReadByteAsync(CancellationToken cancellationToken)
            {
                if (_position == _length)
                {
                    _length = await _stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
                    _position = 0;
                    if (_length == 0)
                    {
                        return -1;
                    }
                }

                return _buffer[_position++];
            }

            /// <summary>Fills <paramref name="destination"/>, or as much of it as the stream holds; answers how much.</summary>
            public async ValueTask<int> ReadFullyAsync(Memory<byte> destination, CancellationToken cancellationToken)
            {
                int fromBuffer = Math.Min(_length - _position, destination.Length);
                _buffer.AsMemory(_position, fromBuffer).CopyTo(destination);
                _position += fromBuffer;
                if (fromBuffer == destination.Length)
                {
                    return fromBuffer;
                }

                return fromBuffer + await _stream.ReadAtLeastAsync(destination[fromBuffer..], destination.Length - fromBuffer, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
