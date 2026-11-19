using ByteShelfCommon;

namespace ByteShelfClient.Tests
{
    public class ByteShelfExceptionTests
    {
        [Theory]
        [InlineData(ErrorCodes.Unauthenticated, true)]
        [InlineData(ErrorCodes.InvalidCredentials, true)]
        [InlineData(ErrorCodes.PermissionDenied, true)]
        [InlineData(ErrorCodes.NotFound, false)]
        [InlineData(ErrorCodes.QuotaExceeded, false)]
        [InlineData(ByteShelfException.InvalidResponse, false)]
        [InlineData(null, false)]
        public void IsAccessRefused_ByCode_IsTrueOnlyForTheCredentialCodes(string? code, bool expected)
        {
            Assert.Equal(expected, new ByteShelfException("refused", code).IsAccessRefused);
        }

        [Theory]
        [InlineData(ErrorCodes.NotFound, true)]
        [InlineData(ErrorCodes.PermissionDenied, false)]
        [InlineData(null, false)]
        public void IsNotFound_ByCode_IsTrueOnlyForNotFound(string? code, bool expected)
        {
            Assert.Equal(expected, new ByteShelfException("refused", code).IsNotFound);
        }

        [Theory]
        [InlineData(ByteShelfException.InvalidResponse, true)]
        [InlineData(ErrorCodes.InvalidRequest, false)]
        [InlineData(null, false)]
        public void IsInvalidResponse_ByCode_IsTrueOnlyForInvalidResponse(string? code, bool expected)
        {
            Assert.Equal(expected, new ByteShelfException("refused", code).IsInvalidResponse);
        }
    }
}
