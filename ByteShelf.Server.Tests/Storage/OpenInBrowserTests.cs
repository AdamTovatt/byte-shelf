using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace ByteShelf.Server.Tests.Storage
{
    /// <summary>
    /// A file opened or saved by a browser's plain navigation, which cannot send a header: under
    /// <c>/api/open</c> the session's token comes in the cookie signing in sets, and nowhere else.
    /// </summary>
    public sealed class OpenInBrowserTests : IAsyncLifetime
    {
        private const string CookieName = "__Secure-access_token";

        private ServerTestHost _host = null!;
        private ShelfFile _file = null!;

        public async Task InitializeAsync()
        {
            _host = await ServerTestHost.StartAsync();
            await _host.CreateShelfAsync("docs");
            HttpResponseMessage stored = await _host.AdminClient.PutAsync("/api/shelves/docs/content?path=/a.txt", new StringContent("the contents", Encoding.UTF8, "text/plain"));
            stored.EnsureSuccessStatusCode();
            _file = (await stored.Content.ReadFromJsonAsync<ShelfFile>())!;
        }

        public async Task DisposeAsync()
        {
            await _host.DisposeAsync();
        }

        [Fact]
        public async Task SignIn_SetsTheCookieForOpeningOnly()
        {
            SetCookieHeaderValue cookie = (await SignInAsync(ServerTestHost.AdminEmail)).Cookie;

            Assert.Equal(CookieName, cookie.Name.Value);
            Assert.Equal("/api/open", cookie.Path.Value);
            Assert.Null(cookie.Domain.Value);
            Assert.True(cookie.Secure);
            Assert.True(cookie.HttpOnly);
            Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
            Assert.InRange(cookie.MaxAge!.Value, TimeSpan.FromMinutes(59), TimeSpan.FromHours(1));
        }

        [Fact]
        public async Task Refresh_SetsAFreshCookieThatOpensFiles()
        {
            SignedIn signedIn = await SignInAsync(ServerTestHost.AdminEmail);

            HttpResponseMessage refresh = await _host.AnonymousClient.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = signedIn.RefreshToken });
            refresh.EnsureSuccessStatusCode();
            SetCookieHeaderValue renewed = SetCookieHeaderValue.Parse(Assert.Single(refresh.Headers.GetValues("Set-Cookie")));

            Assert.Equal(CookieName, renewed.Name.Value);
            Assert.Equal("/api/open", renewed.Path.Value);
            HttpResponseMessage opened = await SendWithCookieAsync(HttpMethod.Get, OpenUri(), renewed);
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        }

        [Fact]
        public async Task Logout_ExpiresTheCookie()
        {
            SignedIn signedIn = await SignInAsync(ServerTestHost.AdminEmail);

            HttpResponseMessage logout = await _host.AnonymousClient.PostAsJsonAsync("/api/auth/logout", new { refreshToken = signedIn.RefreshToken });
            logout.EnsureSuccessStatusCode();
            SetCookieHeaderValue cleared = SetCookieHeaderValue.Parse(Assert.Single(logout.Headers.GetValues("Set-Cookie")));

            Assert.Equal(CookieName, cleared.Name.Value);
            Assert.Equal("/api/open", cleared.Path.Value);
            Assert.True(cleared.MaxAge <= TimeSpan.Zero || cleared.Expires <= DateTimeOffset.UtcNow);
        }

        [Fact]
        public async Task GetOpenContent_WithOnlyTheCookie_ServesTheFile()
        {
            HttpResponseMessage response = await SendWithCookieAsync(HttpMethod.Get, OpenUri() + "&download=true", (await SignInAsync(ServerTestHost.AdminEmail)).Cookie);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("the contents", await response.Content.ReadAsStringAsync());
            Assert.Equal("a.txt", response.Content.Headers.ContentDisposition?.FileName);
        }

        [Fact]
        public async Task GetOpenDeletedContent_WithOnlyTheCookie_SavesTheFileFromTheTrash()
        {
            TrashEntry deleted = (await (await _host.AdminClient.DeleteAsync($"/api/shelves/docs/files?id={_file.Id}")).Content.ReadFromJsonAsync<TrashEntry>())!;

            HttpResponseMessage response = await SendWithCookieAsync(
                HttpMethod.Get, $"/api/open/docs/trash/{deleted.DeletionId}/content?id={_file.Id}&download=true", (await SignInAsync(ServerTestHost.AdminEmail)).Cookie);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("the contents", await response.Content.ReadAsStringAsync());
            Assert.Equal("a.txt", response.Content.Headers.ContentDisposition?.FileName);
        }

        [Fact]
        public async Task GetOpenContent_MayBeCachedOnlyByTheBrowserThatAsked()
        {
            HttpResponseMessage response = await SendWithCookieAsync(HttpMethod.Get, OpenUri(), (await SignInAsync(ServerTestHost.AdminEmail)).Cookie);

            Assert.True(response.Headers.CacheControl?.Private);
        }

        [Fact]
        public async Task HeadOpenContent_WithOnlyTheCookie_AnswersTheFilesLength()
        {
            HttpResponseMessage response = await SendWithCookieAsync(HttpMethod.Head, OpenUri(), (await SignInAsync(ServerTestHost.AdminEmail)).Cookie);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(_file.SizeBytes, response.Content.Headers.ContentLength);
        }

        [Fact]
        public async Task GetOpenContent_AsAUserWithoutAccessToTheShelf_Returns404()
        {
            HttpResponseMessage response = await SendWithCookieAsync(HttpMethod.Get, OpenUri(), (await SignInAsync(ServerTestHost.MemberEmail)).Cookie);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetOpenContent_WithoutTheCookie_Returns401()
        {
            HttpResponseMessage response = await _host.AnonymousClient.GetAsync(OpenUri());

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetShelfContent_WithOnlyTheCookie_Returns401()
        {
            HttpResponseMessage response = await SendWithCookieAsync(HttpMethod.Get, $"/api/shelves/docs/content?id={_file.Id}", (await SignInAsync(ServerTestHost.AdminEmail)).Cookie);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        /// <summary>The cookie carries the session to any route under its path, and a page on another site can make a browser send it: only reads may be there.</summary>
        [Fact]
        public void RoutesUnderTheCookiePath_OnlyRead()
        {
            string cookiePath = AuthSetup.CookieTokenPath.TrimStart('/');
            IEnumerable<RouteEndpoint> underIt = _host.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith(cookiePath, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            Assert.NotEmpty(underIt);
            Assert.All(underIt, endpoint => Assert.All(
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["any method"],
                method => Assert.Contains(method, new[] { HttpMethods.Get, HttpMethods.Head })));
        }

        private string OpenUri()
        {
            return $"/api/open/docs/content?id={_file.Id}";
        }

        /// <summary>What signing in answers: the cookie it sets and the refresh token in its body.</summary>
        private sealed record SignedIn(SetCookieHeaderValue Cookie, string RefreshToken);

        private async Task<SignedIn> SignInAsync(string email)
        {
            HttpResponseMessage login = await _host.AnonymousClient.PostAsJsonAsync("/api/auth/login", new { username = email, password = ServerTestHost.Password });
            login.EnsureSuccessStatusCode();
            using JsonDocument body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            return new SignedIn(
                SetCookieHeaderValue.Parse(Assert.Single(login.Headers.GetValues("Set-Cookie"))),
                body.RootElement.GetProperty("refreshToken").GetString()!);
        }

        /// <summary>A request carrying <paramref name="cookie"/> as a browser sends it, and no other credential.</summary>
        private async Task<HttpResponseMessage> SendWithCookieAsync(HttpMethod method, string uri, SetCookieHeaderValue cookie)
        {
            using HttpRequestMessage request = new HttpRequestMessage(method, uri);
            request.Headers.Add("Cookie", $"{cookie.Name}={cookie.Value}");
            return await _host.AnonymousClient.SendAsync(request);
        }
    }
}
