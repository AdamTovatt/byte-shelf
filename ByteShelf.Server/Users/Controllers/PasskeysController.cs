using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.RateLimiting;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ByteShelf.Server.Users.Controllers
{
    /// <summary>The signed-in user's passkeys: adding one, and listing, renaming and removing them.</summary>
    [ApiController]
    [Route("api/users/me/passkeys")]
    [Authorize]
    public class PasskeysController : ControllerBase
    {
        private readonly PasskeyService _passkeyService;

        public PasskeysController(PasskeyService passkeyService)
        {
            _passkeyService = passkeyService;
        }

        /// <summary>The signed-in user's passkeys, oldest first, and whether passkeys are on here.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(PasskeyList), 200)]
        public async Task<IActionResult> List()
        {
            return Ok(await _passkeyService.ListAsync(HttpContext.GetUserGuid()));
        }

        /// <summary>
        /// Begins adding a passkey, with the user's current password: the options for the browser's prompt.
        /// A wrong password is 401 <c>invalid_credentials</c> without a challenge, as changing the password
        /// answers it; 404 while passkeys are off.
        /// </summary>
        [HttpPost("ceremony")]
        [EnableRateLimiting(RateLimitPolicies.Strict)]
        [ProducesResponseType(typeof(PasskeyCeremony), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> BeginAdding([FromBody] BeginAddingPasskeyRequest request)
        {
            PasskeyCeremony? ceremony = await _passkeyService.BeginAddingAsync(HttpContext.GetUserGuid(), request.CurrentPassword);
            if (ceremony == null)
            {
                return Unauthorized(new ErrorResponse { Message = "The current password is incorrect.", Code = ErrorCodes.InvalidCredentials });
            }

            return Ok(ceremony);
        }

        /// <summary>
        /// Adds the passkey the browser made. 400 for a bad name, a ceremony that is not open, or a passkey
        /// that does not verify; 409 for one already added.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(PasskeyInfo), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Add([FromBody] AddPasskeyRequest request)
        {
            return Ok(await _passkeyService.AddAsync(HttpContext.GetUserGuid(), request));
        }

        /// <summary>Renames one of the signed-in user's passkeys.</summary>
        [HttpPut("{passkeyId:guid}/name")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Rename(Guid passkeyId, [FromBody] RenamePasskeyRequest request)
        {
            await _passkeyService.RenameAsync(HttpContext.GetUserGuid(), passkeyId, request.Name);
            return NoContent();
        }

        /// <summary>Removes one of the signed-in user's passkeys, ending the sessions signed in with it.</summary>
        [HttpDelete("{passkeyId:guid}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Remove(Guid passkeyId)
        {
            await _passkeyService.RemoveAsync(HttpContext.GetUserGuid(), passkeyId);
            return NoContent();
        }
    }
}
