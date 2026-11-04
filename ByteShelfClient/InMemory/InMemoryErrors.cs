using ByteShelfCommon;

namespace ByteShelfClient.InMemory
{
    /// <summary>The errors the in-memory client throws, with the codes the server would answer.</summary>
    internal static class InMemoryErrors
    {
        public static ByteShelfException NotFound(string message)
        {
            return new ByteShelfException(message, ErrorCodes.NotFound);
        }

        public static ByteShelfException Conflict(string message)
        {
            return new ByteShelfException(message, ErrorCodes.Conflict);
        }

        public static ByteShelfException Invalid(string message)
        {
            return new ByteShelfException(message, ErrorCodes.InvalidRequest);
        }

        /// <summary>
        /// Runs a check the server makes, turning its <see cref="ArgumentException"/> into the
        /// <see cref="ErrorCodes.InvalidRequest"/> the server would answer.
        /// </summary>
        public static T Checked<T>(Func<T> check)
        {
            try
            {
                return check();
            }
            catch (ArgumentException exception)
            {
                throw new ByteShelfException(exception.Message, ErrorCodes.InvalidRequest, statusCode: null, exception);
            }
        }

        /// <inheritdoc cref="Checked{T}(Func{T})"/>
        public static void Checked(Action check)
        {
            Checked(() =>
            {
                check();
                return true;
            });
        }
    }
}
