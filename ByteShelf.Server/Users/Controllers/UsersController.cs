using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Shared.RateLimiting;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ByteShelf.Server.Users.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }

        /// <summary>The signed-in user.</summary>
        [HttpGet("me")]
        [Authorize]
        [ProducesResponseType(typeof(User), 200)]
        public async Task<IActionResult> GetMe()
        {
            Guid userId = HttpContext.GetUserGuid();

            User user = await _userService.GetUserByIdAsync(userId)
                ?? throw new NotFoundException("The signed-in user no longer exists.");

            return Ok(user);
        }

        /// <summary>
        /// Changes the signed-in user's password after verifying the current one. A wrong current
        /// password answers 401 so it counts towards the delay on repeated failed sign-ins.
        /// </summary>
        [HttpPut("me/password")]
        [Authorize]
        [EnableRateLimiting(RateLimitPolicies.Strict)]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> UpdateMyPassword([FromBody] UpdatePasswordRequest request)
        {
            Guid userId = HttpContext.GetUserGuid();

            if (!await _userService.UpdatePasswordAsync(userId, request.CurrentPassword, request.NewPassword))
            {
                return Unauthorized(new ErrorResponse { Message = "The current password is incorrect.", Code = ErrorCodes.InvalidCredentials });
            }

            return NoContent();
        }

        /// <summary>Every user. Admin only.</summary>
        [HttpGet]
        [Authorize(Roles = UserRole.Admin)]
        [ProducesResponseType(typeof(IReadOnlyList<User>), 200)]
        public async Task<IActionResult> GetAll()
        {
            return Ok(await _userService.GetAllUsersAsync());
        }

        /// <summary>Creates a user. Admin only.</summary>
        [HttpPost]
        [Authorize(Roles = UserRole.Admin)]
        [ProducesResponseType(typeof(User), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
        {
            User user = await _userService.CreateUserAsync(
                email: request.Email,
                displayName: request.DisplayName,
                password: request.Password,
                roles: request.Roles);

            return Ok(user);
        }

        /// <summary>
        /// Replaces a user's roles. Admin only. Takes effect on the user's next token refresh; the last
        /// admin cannot lose the role.
        /// </summary>
        [HttpPut("{userId:guid}/roles")]
        [Authorize(Roles = UserRole.Admin)]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> UpdateRoles(Guid userId, [FromBody] UpdateRolesRequest request)
        {
            await _userService.UpdateRolesAsync(userId, request.Roles);
            return NoContent();
        }
    }
}
