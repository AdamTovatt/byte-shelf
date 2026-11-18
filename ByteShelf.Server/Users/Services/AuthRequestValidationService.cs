using ByteShelf.Server.Users.Models;
using EasyReasy.Auth;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// Validates password sign-in requests and starts the session (<see cref="SessionIssuer"/>). The
    /// credential carried by the login request is the <b>email</b> (EasyReasy.Auth's request field is
    /// named <c>Username</c>).
    /// </summary>
    public class AuthRequestValidationService : IAuthRequestValidationService
    {
        private readonly IUserService _userService;
        private readonly SessionIssuer _sessionIssuer;

        public AuthRequestValidationService(IUserService userService, SessionIssuer sessionIssuer)
        {
            _userService = userService;
            _sessionIssuer = sessionIssuer;
        }

        /// <summary>
        /// Programs authenticate with shelf keys sent on each request, not through this endpoint, which
        /// is not mapped; answers a failure if it is ever called.
        /// </summary>
        public Task<ApiKeyAuthResult> ValidateApiKeyRequestAsync(
            ApiKeyAuthRequest request,
            IJwtTokenService jwtTokenService,
            HttpContext? httpContext = null)
        {
            return Task.FromResult(ApiKeyAuthResult.Failed(ApiKeyAuthFailureReason.UnknownKey, attemptedClientId: null));
        }

        /// <inheritdoc />
        public async Task<LoginResult> ValidateLoginRequestAsync(
            LoginAuthRequest request,
            IJwtTokenService jwtTokenService,
            HttpContext? httpContext = null)
        {
            UserCredentials? credentials = await _userService.ValidateCredentialsAsync(request.Username, request.Password);
            if (credentials != null && await _sessionIssuer.IssueAsync(credentials, passkeyId: null, jwtTokenService) is AuthResponse session)
            {
                return LoginResult.Succeeded(session, subject: credentials.User.Id.ToString("D"));
            }

            return LoginResult.Failed(LoginFailureReason.InvalidCredentials, attemptedSubject: request.Username);
        }
    }
}
