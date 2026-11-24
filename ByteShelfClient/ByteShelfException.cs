using System.Net;
using ByteShelfCommon;

namespace ByteShelfClient
{
    /// <summary>
    /// A request the server refused, with its reason, or an answer the client could not read as one ByteShelf
    /// sends (<see cref="InvalidResponse"/>). A request that never reached the server throws what
    /// <see cref="HttpClient"/> throws instead.
    /// </summary>
    public sealed class ByteShelfException : Exception
    {
        /// <summary>
        /// The <see cref="Code"/> ByteShelfClient gives a successful answer whose body is not ByteShelf's. The server
        /// never sends it, so it is not one of <see cref="ErrorCodes"/>.
        /// </summary>
        public const string InvalidResponse = "invalid_response";

        /// <summary>An error with the server's message, code and status.</summary>
        public ByteShelfException(string message, string? code, HttpStatusCode? statusCode = null, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
            StatusCode = statusCode;
        }

        /// <summary>An error without a code.</summary>
        public ByteShelfException()
        {
        }

        /// <summary>An error with a message but no code.</summary>
        public ByteShelfException(string message)
            : base(message)
        {
        }

        /// <summary>An error with a message and its cause, but no code.</summary>
        public ByteShelfException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// One of <see cref="ErrorCodes"/>, or <see cref="InvalidResponse"/>, for a program to act on; null when there
        /// is none.
        /// </summary>
        public string? Code { get; }

        /// <summary>The HTTP status the server answered with; null for the in-memory client.</summary>
        public HttpStatusCode? StatusCode { get; }

        /// <summary>
        /// Whether the server refused who is asking, or a password or key they gave, rather than what they asked:
        /// no valid credentials (<see cref="ErrorCodes.Unauthenticated"/>), a wrong password or key
        /// (<see cref="ErrorCodes.InvalidCredentials"/>), or access that does not allow it
        /// (<see cref="ErrorCodes.PermissionDenied"/>). Asking again with the same credentials gets the same answer.
        /// </summary>
        public bool IsAccessRefused => Code
            is ErrorCodes.Unauthenticated
            or ErrorCodes.InvalidCredentials
            or ErrorCodes.PermissionDenied;

        /// <summary>
        /// Whether the server answered that nothing is at what was named, or nothing the caller may see
        /// (<see cref="ErrorCodes.NotFound"/>).
        /// </summary>
        public bool IsNotFound => Code == ErrorCodes.NotFound;

        /// <summary>
        /// Whether what was asked for is still being made (<see cref="ErrorCodes.NotReady"/>): asked for again in a while,
        /// it may be there.
        /// </summary>
        public bool IsNotReady => Code == ErrorCodes.NotReady;

        /// <summary>
        /// Whether a successful answer's body was not ByteShelf's (<see cref="InvalidResponse"/>).
        /// </summary>
        public bool IsInvalidResponse => Code == InvalidResponse;
    }
}
