using System.Buffers.Text;
using System.Text.Json;
using ByteShelf.Server.Shared.Json;

namespace ByteShelf.Server.Replication.Models
{
    /// <summary>
    /// What a replica's admin pastes to pair it with a primary: the primary's address, its instance id
    /// and a one-time secret, written as <c>bsrep1_</c> and base64url JSON.
    /// </summary>
    public sealed class PairingCode
    {
        public const string Prefix = "bsrep1_";

        public required string PrimaryUrl { get; init; }

        public required string InstanceId { get; init; }

        public required string Secret { get; init; }

        public override string ToString()
        {
            return Prefix + Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(this, JsonDefaults.CamelCase));
        }

        /// <summary>The code <paramref name="text"/> holds, or null when it is not one.</summary>
        public static PairingCode? TryParse(string? text)
        {
            string trimmed = text?.Trim() ?? string.Empty;
            if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal) || !Base64Url.IsValid(trimmed.AsSpan(Prefix.Length)))
            {
                return null;
            }

            try
            {
                PairingCode? code = JsonSerializer.Deserialize<PairingCode>(Base64Url.DecodeFromChars(trimmed.AsSpan(Prefix.Length)), JsonDefaults.CamelCase);
                bool usable = code != null
                    && Uri.TryCreate(code.PrimaryUrl, UriKind.Absolute, out Uri? url)
                    && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
                    && !string.IsNullOrEmpty(code.InstanceId)
                    && !string.IsNullOrEmpty(code.Secret);
                return usable ? code : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
