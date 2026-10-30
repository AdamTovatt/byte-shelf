using ByteShelf.Server.Shared.RateLimiting;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ByteShelf.Server.Users.Controllers
{
    [ApiController]
    [Route("api/password-reset")]
    public class PasswordResetController : ControllerBase
    {
        private readonly IPasswordResetService _passwordResetService;

        public PasswordResetController(IPasswordResetService passwordResetService)
        {
            _passwordResetService = passwordResetService;
        }

        /// <summary>
        /// Requests a reset link for an email address. Answers 204 whether or not the address has an
        /// account, so the endpoint cannot be used to find out.
        /// </summary>
        [HttpPost("request")]
        [EnableRateLimiting(RateLimitPolicies.VeryStrict)]
        [ProducesResponseType(204)]
        public async Task<IActionResult> RequestReset([FromBody] RequestPasswordResetRequest request)
        {
            await _passwordResetService.RequestResetAsync(request.Email);
            return NoContent();
        }

        /// <summary>Sets a new password with a token from a reset link, and signs the user out everywhere.</summary>
        [HttpPost("complete")]
        [EnableRateLimiting(RateLimitPolicies.VeryStrict)]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        public async Task<IActionResult> CompleteReset([FromBody] CompletePasswordResetRequest request)
        {
            if (!await _passwordResetService.CompleteResetAsync(request.Token, request.NewPassword))
            {
                return BadRequest(new ErrorResponse { Message = "The reset link is invalid, used or expired.", Code = ErrorCodes.InvalidRequest });
            }

            return NoContent();
        }
    }
}
