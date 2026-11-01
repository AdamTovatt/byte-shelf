using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Checks a signed-in user's access token against the database once per request, as it is validated:
    /// the request gets the roles the user has now rather than the ones the token was minted with, and a
    /// token whose user is gone fails to authenticate. So <c>[Authorize(Roles)]</c> and <c>IsInRole</c> see
    /// the database, and a demoted or deleted admin's token loses admin at once.
    /// </summary>
    /// <remarks>
    /// Done while the token is validated rather than in an <c>IClaimsTransformation</c>, which cannot fail
    /// authentication: a deleted user's token would authenticate with no identity, and endpoints whose
    /// policy names its schemes, the shelf endpoints among them, would answer 403 instead of 401.
    /// </remarks>
    public static class CurrentUserTokenValidation
    {
        /// <summary>Wraps <paramref name="events"/>' own <c>OnTokenValidated</c>, which still runs first.</summary>
        public static void AddTo(JwtBearerEvents events)
        {
            Func<TokenValidatedContext, Task> earlier = events.OnTokenValidated;
            events.OnTokenValidated = async context =>
            {
                await earlier(context);
                if (context.Result == null)
                {
                    await ApplyAsync(context);
                }
            };
        }

        private static async Task ApplyAsync(TokenValidatedContext context)
        {
            ClaimsIdentity? identity = context.Principal?.Identity as ClaimsIdentity;

            // Where the token's subject lands depends on inbound claim mapping; EasyReasy.Auth reads both.
            string? subject = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? identity?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (identity == null || !Guid.TryParse(subject, out Guid userId))
            {
                context.Fail("The token names no user.");
                return;
            }

            User? user = await context.HttpContext.RequestServices.GetRequiredService<IUserRepository>().GetByIdAsync(userId);
            if (user == null)
            {
                context.Fail("The user no longer exists.");
                return;
            }

            foreach (Claim role in identity.FindAll(identity.RoleClaimType).ToList())
            {
                identity.RemoveClaim(role);
            }

            foreach (string role in user.Roles)
            {
                identity.AddClaim(new Claim(identity.RoleClaimType, role));
            }
        }
    }
}
