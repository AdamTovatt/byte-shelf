using ByteShelf.Server.Replication.Controllers;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using EasyReasy.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Users.Controllers
{
    /// <summary>
    /// Signing in with a passkey alone, for anyone. Both requests count toward the strict sign-in rate
    /// limit with password sign-in (<see cref="AuthSetup.IsSignIn"/>), a refused one toward the
    /// progressive sign-in delay, and both work on a replica, which records the sign-in locally only.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    public class PasskeySignInController : ControllerBase
    {
        private readonly PasskeyService _passkeyService;
        private readonly IJwtTokenService _jwtTokenService;

        public PasskeySignInController(PasskeyService passkeyService, IJwtTokenService jwtTokenService)
        {
            _passkeyService = passkeyService;
            _jwtTokenService = jwtTokenService;
        }

        /// <summary>Begins a passkey sign-in: the options for the browser's prompt. 404 while passkeys are off.</summary>
        [HttpPost(AuthSetup.PasskeyCeremonyRoute)]
        [AllowedOnReplica]
        [ProducesResponseType(typeof(PasskeyCeremony), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public IActionResult BeginSignIn()
        {
            return Ok(_passkeyService.BeginSignIn());
        }

        /// <summary>
        /// Signs in with the passkey that answered the ceremony: the same tokens a password sign-in
        /// answers. 401 when the ceremony is not open or the passkey is unknown or does not verify.
        /// </summary>
        [HttpPost(AuthSetup.PasskeySignInRoute)]
        [AllowedOnReplica]
        [ProducesResponseType(typeof(AuthResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> SignIn([FromBody] PasskeySignInRequest request)
        {
            HttpContext.SetNoStoreCacheControl();

            AuthResponse? session = await _passkeyService.SignInAsync(request, _jwtTokenService);
            if (session == null)
            {
                return Unauthorized(new ErrorResponse { Message = "The passkey could not sign you in.", Code = ErrorCodes.InvalidCredentials });
            }

            // After the session is committed, as the password sign-in sets it, and only now clears the
            // address's failed sign-ins, which the progressive delay counts from every 401 here too.
            HttpContext.TrySetAccessTokenCookie(session.Token);
            HttpContext.MarkAuthenticationSucceeded();
            return Ok(session);
        }
    }
}
