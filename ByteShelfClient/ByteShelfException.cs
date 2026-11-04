using System.Net;
using ByteShelfCommon;

namespace ByteShelfClient
{
    /// <summary>
    /// A request the server refused, with its reason. A request that never reached the server throws
    /// what <see cref="HttpClient"/> throws instead.
    /// </summary>
    public sealed class ByteShelfException : Exception
    {
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

        /// <summary>One of <see cref="ErrorCodes"/>, for a program to act on; null when there is none.</summary>
        public string? Code { get; }

        /// <summary>The HTTP status the server answered with; null for the in-memory client.</summary>
        public HttpStatusCode? StatusCode { get; }
    }
}
