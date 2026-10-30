using EasyReasy.Auth;

namespace ByteShelf.Server.Shared.Auth
{
    /// <summary>
    /// A no-op implementation of <see cref="IAuthRequestValidationService"/> used exclusively
    /// in schema-only mode. All validation methods return failed results since no real
    /// authentication is performed — the endpoints exist only to appear in the OpenAPI spec.
    /// </summary>
    public class NoOpAuthRequestValidationService : IAuthRequestValidationService
    {
        public Task<ApiKeyAuthResult> ValidateApiKeyRequestAsync(
            ApiKeyAuthRequest request,
            IJwtTokenService jwtTokenService,
            HttpContext? httpContext = null)
        {
            return Task.FromResult(ApiKeyAuthResult.Failed(ApiKeyAuthFailureReason.UnknownKey, attemptedClientId: null));
        }

        public Task<LoginResult> ValidateLoginRequestAsync(
            LoginAuthRequest request,
            IJwtTokenService jwtTokenService,
            HttpContext? httpContext = null)
        {
            return Task.FromResult(LoginResult.Failed(LoginFailureReason.InvalidCredentials, attemptedSubject: request.Username));
        }
    }
}
