using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyReasy.Auth;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// A passkey authenticator in software, for driving the passkey endpoints against the real verifier: an
    /// ES256 key pair that answers the options the server hands a browser the way a platform authenticator
    /// does, at <see cref="Origin"/>, with attestation <c>none</c>. It keeps the user handle it registered
    /// with and answers it back, unless a test sets another.
    /// </summary>
    /// <remarks>
    /// EasyReasy.Auth's tests hold the same thing, <c>SyntheticAuthenticator</c>, internal to them; a
    /// testing package publishing it would replace this.
    /// </remarks>
    public sealed class SoftwareAuthenticator : IDisposable
    {
        /// <summary>The origin of the server a test host gives a public address to (<see cref="ServerTestHost.PasskeyAddress"/>).</summary>
        public const string Origin = "http://localhost";

        /// <summary>The transports every registration reports, as a phone's platform authenticator would.</summary>
        public static readonly IReadOnlyList<string> Transports = ["internal", "hybrid"];

        private const byte UserPresent = 0x01;
        private const byte UserVerifiedFlag = 0x04;
        private const byte AttestedCredentialData = 0x40;

        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        /// <summary>The credential id, random for each authenticator.</summary>
        public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(16);

        /// <summary>The user handle answered with every assertion: the one registered with, until a test sets another.</summary>
        public byte[]? UserHandle { get; set; }

        /// <summary>The sign count the next answer reports; 0 for an authenticator that keeps none.</summary>
        public uint SignCount { get; set; }

        /// <summary>Whether the user was verified (fingerprint, face, PIN), or only present.</summary>
        public bool VerifiesUser { get; set; } = true;

        /// <summary>What <c>PublicKeyCredential.toJSON()</c> gives for a passkey made from <paramref name="creationOptions"/>.</summary>
        public JsonElement Register(JsonElement creationOptions)
        {
            string relyingPartyId = creationOptions.GetProperty("rp").GetProperty("id").GetString()!;
            UserHandle = Base64Url.DecodeFromChars(creationOptions.GetProperty("user").GetProperty("id").GetString()!);

            byte[] clientData = ClientData("webauthn.create", creationOptions.GetProperty("challenge").GetString()!);
            byte[] authenticatorData = AuthenticatorData(relyingPartyId, AttestedCredentialData, [.. AttestedCredential()]);

            CborWriter attestation = new CborWriter(CborConformanceMode.Ctap2Canonical);
            attestation.WriteStartMap(3);
            attestation.WriteTextString("fmt");
            attestation.WriteTextString("none");
            attestation.WriteTextString("attStmt");
            attestation.WriteStartMap(0);
            attestation.WriteEndMap();
            attestation.WriteTextString("authData");
            attestation.WriteByteString(authenticatorData);
            attestation.WriteEndMap();

            WebAuthnRegistrationResponse response = new WebAuthnRegistrationResponse(
                CredentialIdText,
                CredentialIdText,
                "public-key",
                new WebAuthnAttestationResponse(Base64Url.EncodeToString(clientData), Base64Url.EncodeToString(attestation.Encode()), Transports),
                "platform");
            return Parse(response.ToJson());
        }

        /// <summary>What <c>PublicKeyCredential.toJSON()</c> gives for this passkey answering <paramref name="requestOptions"/>.</summary>
        public JsonElement Assert(JsonElement requestOptions)
        {
            string relyingPartyId = requestOptions.GetProperty("rpId").GetString()!;
            byte[] clientData = ClientData("webauthn.get", requestOptions.GetProperty("challenge").GetString()!);
            byte[] authenticatorData = AuthenticatorData(relyingPartyId, 0, []);
            byte[] signature = _key.SignData([.. authenticatorData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

            WebAuthnAuthenticationResponse response = new WebAuthnAuthenticationResponse(
                CredentialIdText,
                CredentialIdText,
                "public-key",
                new WebAuthnAssertionResponse(
                    Base64Url.EncodeToString(clientData),
                    Base64Url.EncodeToString(authenticatorData),
                    Base64Url.EncodeToString(signature),
                    UserHandle == null ? null : Base64Url.EncodeToString(UserHandle)),
                "platform");
            return Parse(response.ToJson());
        }

        public void Dispose()
        {
            _key.Dispose();
        }

        private string CredentialIdText => Base64Url.EncodeToString(CredentialId);

        private static byte[] ClientData(string type, string challenge)
        {
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge, origin = Origin, crossOrigin = false }));
        }

        private byte[] AuthenticatorData(string relyingPartyId, byte extraFlags, byte[] attestedCredential)
        {
            byte flags = (byte)(UserPresent | (VerifiesUser ? UserVerifiedFlag : 0) | extraFlags);
            byte[] signCount = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(signCount, SignCount);
            return [.. SHA256.HashData(Encoding.UTF8.GetBytes(relyingPartyId)), flags, .. signCount, .. attestedCredential];
        }

        /// <summary>The AAGUID (zero, as for attestation none), the credential id with its length, and the COSE public key.</summary>
        private IEnumerable<byte> AttestedCredential()
        {
            ECParameters parameters = _key.ExportParameters(includePrivateParameters: false);

            CborWriter coseKey = new CborWriter(CborConformanceMode.Ctap2Canonical);
            coseKey.WriteStartMap(5);
            coseKey.WriteInt32(1);
            coseKey.WriteInt32(2);
            coseKey.WriteInt32(3);
            coseKey.WriteInt32(-7);
            coseKey.WriteInt32(-1);
            coseKey.WriteInt32(1);
            coseKey.WriteInt32(-2);
            coseKey.WriteByteString(parameters.Q.X!);
            coseKey.WriteInt32(-3);
            coseKey.WriteByteString(parameters.Q.Y!);
            coseKey.WriteEndMap();

            byte[] length = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)CredentialId.Length);
            return [.. new byte[16], .. length, .. CredentialId, .. coseKey.Encode()];
        }

        private static JsonElement Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}
