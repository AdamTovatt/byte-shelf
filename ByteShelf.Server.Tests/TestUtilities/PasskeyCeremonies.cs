using System.Net.Http.Json;
using ByteShelf.Server.Users.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>The passkey ceremonies as the web UI runs them, with a <see cref="SoftwareAuthenticator"/> in the browser's place.</summary>
    public static class PasskeyCeremonies
    {
        /// <summary>The server's answer to beginning to add a passkey as <paramref name="client"/>'s user, giving <paramref name="password"/>.</summary>
        public static Task<HttpResponseMessage> TryBeginAddingAsync(HttpClient client, string password = ServerTestHost.Password)
        {
            return client.PostAsJsonAsync("/api/users/me/passkeys/ceremony", new { currentPassword = password });
        }

        /// <summary>Begins adding a passkey as <paramref name="client"/>'s user, and answers the ceremony.</summary>
        public static async Task<PasskeyCeremony> BeginAddingAsync(HttpClient client)
        {
            HttpResponseMessage begun = await TryBeginAddingAsync(client);
            begun.EnsureSuccessStatusCode();
            return (await begun.Content.ReadFromJsonAsync<PasskeyCeremony>())!;
        }

        /// <summary>The server's answer to adding the passkey <paramref name="authenticator"/> makes for the ceremony.</summary>
        public static Task<HttpResponseMessage> TryAddAsync(HttpClient client, PasskeyCeremony ceremony, SoftwareAuthenticator authenticator, string name = "Test laptop")
        {
            return client.PostAsJsonAsync("/api/users/me/passkeys", new
            {
                ceremonyId = ceremony.CeremonyId,
                credential = authenticator.Register(ceremony.Options),
                name,
            });
        }

        /// <summary>Adds the passkey <paramref name="authenticator"/> makes to <paramref name="client"/>'s user.</summary>
        public static async Task<PasskeyInfo> AddAsync(HttpClient client, SoftwareAuthenticator authenticator, string name = "Test laptop")
        {
            HttpResponseMessage added = await TryAddAsync(client, await BeginAddingAsync(client), authenticator, name);
            added.EnsureSuccessStatusCode();
            return (await added.Content.ReadFromJsonAsync<PasskeyInfo>())!;
        }

        /// <summary>Begins a passkey sign-in, and answers the ceremony.</summary>
        public static async Task<PasskeyCeremony> BeginSignInAsync(HttpClient client)
        {
            HttpResponseMessage begun = await client.PostAsync("/api/auth/passkey/ceremony", content: null);
            begun.EnsureSuccessStatusCode();
            return (await begun.Content.ReadFromJsonAsync<PasskeyCeremony>())!;
        }

        /// <summary>The server's answer to <paramref name="authenticator"/> signing the ceremony's challenge.</summary>
        public static Task<HttpResponseMessage> TrySignInAsync(HttpClient client, PasskeyCeremony ceremony, SoftwareAuthenticator authenticator)
        {
            return client.PostAsJsonAsync("/api/auth/passkey", new { ceremonyId = ceremony.CeremonyId, credential = authenticator.Assert(ceremony.Options) });
        }

        /// <summary>The server's answer to a whole passkey sign-in with <paramref name="authenticator"/>.</summary>
        public static async Task<HttpResponseMessage> TrySignInAsync(HttpClient client, SoftwareAuthenticator authenticator)
        {
            return await TrySignInAsync(client, await BeginSignInAsync(client), authenticator);
        }

        /// <summary>The tokens of a passkey sign-in with <paramref name="authenticator"/>.</summary>
        public static async Task<SessionTokens> SignInAsync(HttpClient client, SoftwareAuthenticator authenticator)
        {
            HttpResponseMessage signedIn = await TrySignInAsync(client, authenticator);
            signedIn.EnsureSuccessStatusCode();
            return (await signedIn.Content.ReadFromJsonAsync<SessionTokens>())!;
        }
    }
}
