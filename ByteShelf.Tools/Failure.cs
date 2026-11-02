using ByteShelfClient;
using ByteShelfClient.Encryption;
using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>What to tell a person or an agent when a command or a tool call fails.</summary>
    internal sealed class Failure
    {
        private Failure(string message, bool isMisuse)
        {
            Message = message;
            IsMisuse = isMisuse;
        }

        public string Message { get; }

        /// <summary>Whether it was asked for wrongly, or cannot run as things are, rather than refused or broken.</summary>
        public bool IsMisuse { get; }

        /// <summary>
        /// The failure <paramref name="exception"/> stands for, or null for one nothing expects, which is
        /// a bug to surface whole. A cancellation the caller asked for is not a failure; check for it first.
        /// </summary>
        public static Failure? From(Exception exception)
        {
            return exception switch
            {
                UsageException or ArgumentException => new Failure(exception.Message, isMisuse: true),
                ByteShelfException refused => new Failure(
                    $"{refused.Message} ({refused.Code ?? "error"})" + (refused.Code == ErrorCodes.Unauthenticated ? " Sign in again with 'byteshelf login'." : string.Empty),
                    isMisuse: false),
                HttpRequestException unreachable => new Failure($"Could not reach the server: {unreachable.Message}", isMisuse: false),
                OperationCanceledException => new Failure("The server did not answer in time.", isMisuse: false),
                IOException or UnauthorizedAccessException or TimeoutException => new Failure(exception.Message, isMisuse: false),

                // A wrong password is given wrongly; a damaged file is broken.
                AgeException age => new Failure(age.Message, isMisuse: age.Failure == AgeFailure.NoMatch),
                PlatformNotSupportedException => new Failure(exception.Message, isMisuse: false),
                _ => null,
            };
        }
    }
}
