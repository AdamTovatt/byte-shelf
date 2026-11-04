using ByteShelf.Server.Access.Auth;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace ByteShelf.Server.Tests.Access
{
    /// <summary>
    /// The header checks, run on the handler itself: the in-memory test client drops an empty header
    /// before it reaches the server, though a real client can send one.
    /// </summary>
    public class ShelfKeyAuthenticationHandlerTests : ServiceTestBase
    {
        [Fact]
        public async Task AuthenticateAsync_EmptyHeader_Fails()
        {
            AuthenticateResult result = await AuthenticateAsync(new StringValues(string.Empty));

            Assert.NotNull(result.Failure);
        }

        [Fact]
        public async Task AuthenticateAsync_HeaderTwiceWithAValidSecret_Fails()
        {
            Shelf shelf = await CreateShelfAsync();
            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read);

            AuthenticateResult result = await AuthenticateAsync(new StringValues(new[] { created.Secret, created.Secret }));

            Assert.NotNull(result.Failure);
        }

        [Fact]
        public async Task AuthenticateAsync_ValidSecret_SucceedsAsTheKey()
        {
            Shelf shelf = await CreateShelfAsync();
            CreatedShelfKey created = await KeyService.CreateAsync(shelf, "app", ShelfKeyPermission.Read);
            DefaultHttpContext context = new DefaultHttpContext();

            AuthenticateResult result = await AuthenticateAsync(new StringValues(created.Secret), context);

            Assert.True(result.Succeeded);
            Assert.Equal(created.Key.Id, ShelfKeyAuthenticationHandler.GetKey(context)?.Id);
        }

        private async Task<AuthenticateResult> AuthenticateAsync(StringValues header, DefaultHttpContext? context = null)
        {
            context ??= new DefaultHttpContext();
            context.Request.Headers[ShelfKeys.HeaderName] = header;

            ShelfKeyAuthenticationHandler handler = Services.GetRequiredService<ShelfKeyAuthenticationHandler>();
            await handler.InitializeAsync(new AuthenticationScheme(ShelfKeyAuthentication.Scheme, null, typeof(ShelfKeyAuthenticationHandler)), context);
            return await handler.AuthenticateAsync();
        }
    }
}
