using System.Text.Json;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Shared.Json;
using ByteShelf.Server.Shared.Middleware;
using ByteShelf.Server.Users.Exceptions;
using ByteShelfCommon;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;

namespace ByteShelf.Server.Tests.Shared
{
    public class ExceptionHandlingMiddlewareTests
    {
        public static TheoryData<Exception, int, string?> MappedExceptions => new TheoryData<Exception, int, string?>
        {
            { new AuthenticatedUserException("no user"), StatusCodes.Status401Unauthorized, null },
            { new NotFoundException("gone"), StatusCodes.Status404NotFound, ErrorCodes.NotFound },
            { new PermissionDeniedException("read only"), StatusCodes.Status403Forbidden, ErrorCodes.PermissionDenied },
            { new ConflictException("taken"), StatusCodes.Status409Conflict, ErrorCodes.Conflict },
            { new DuplicateEmailException("a@example.com"), StatusCodes.Status409Conflict, ErrorCodes.Conflict },
            { new QuotaExceededException("full"), StatusCodes.Status507InsufficientStorage, ErrorCodes.QuotaExceeded },
            { new LengthRequiredException("length"), StatusCodes.Status411LengthRequired, ErrorCodes.InvalidRequest },
            { new ArgumentException("bad input"), StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest },
            { new BadHttpRequestException("too big", StatusCodes.Status413PayloadTooLarge), StatusCodes.Status413PayloadTooLarge, ErrorCodes.TooLarge },
        };

        [Theory]
        [MemberData(nameof(MappedExceptions))]
        public async Task InvokeAsync_DomainException_AnswersItsStatusAndCode(Exception exception, int statusCode, string? code)
        {
            (int actualStatus, ErrorResponse body) = await InvokeWithExceptionAsync(exception);

            Assert.Equal(statusCode, actualStatus);
            Assert.Equal(code, body.Code);
        }

        [Fact]
        public async Task InvokeAsync_ArgumentExceptionWithParameterName_LeavesTheNameOut()
        {
            (_, ErrorResponse body) = await InvokeWithExceptionAsync(new ArgumentException("A path is required.", "path"));

            Assert.Equal("A path is required.", body.Message);
        }

        [Fact]
        public async Task InvokeAsync_UnlistedException_Answers500WithoutItsMessage()
        {
            (int statusCode, ErrorResponse body) = await InvokeWithExceptionAsync(new InvalidOperationException("secret detail"));

            Assert.Equal(StatusCodes.Status500InternalServerError, statusCode);
            Assert.DoesNotContain("secret detail", body.Message, StringComparison.Ordinal);
            Assert.Null(body.Code);
        }

        [Fact]
        public async Task InvokeAsync_UnauthorizedAccessException_IsAFaultNotANotFound()
        {
            (int statusCode, _) = await InvokeWithExceptionAsync(new UnauthorizedAccessException("data folder not writable"));

            Assert.Equal(StatusCodes.Status500InternalServerError, statusCode);
        }

        [Fact]
        public async Task InvokeAsync_ClientAborted_WritesNothing()
        {
            using CancellationTokenSource aborted = new CancellationTokenSource();
            await aborted.CancelAsync();
            DefaultHttpContext context = new DefaultHttpContext { RequestAborted = aborted.Token };
            using MemoryStream bodyStream = new MemoryStream();
            context.Response.Body = bodyStream;
            ExceptionHandlingMiddleware middleware = new ExceptionHandlingMiddleware(
                _ => throw new IOException("connection reset"),
                NullLogger<ExceptionHandlingMiddleware>.Instance);

            await middleware.InvokeAsync(context);

            Assert.Equal(0, bodyStream.Length);
        }

        [Fact]
        public async Task InvokeAsync_ResponseAlreadyStarted_DoesNotWriteBody()
        {
            DefaultHttpContext context = new DefaultHttpContext();
            using MemoryStream bodyStream = new MemoryStream();
            context.Response.Body = bodyStream;
            context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature(context.Response.Body));
            ExceptionHandlingMiddleware middleware = new ExceptionHandlingMiddleware(
                _ => throw new InvalidOperationException("stream died mid-flight"),
                NullLogger<ExceptionHandlingMiddleware>.Instance);

            await middleware.InvokeAsync(context);

            // A started response (e.g. the SSE stream) can't carry a JSON error body — the
            // middleware must swallow-and-log instead of throwing a second exception.
            Assert.Equal(0, bodyStream.Length);
        }

        private static async Task<(int StatusCode, ErrorResponse Body)> InvokeWithExceptionAsync(Exception exception)
        {
            DefaultHttpContext context = new DefaultHttpContext();
            using MemoryStream bodyStream = new MemoryStream();
            context.Response.Body = bodyStream;
            ExceptionHandlingMiddleware middleware = new ExceptionHandlingMiddleware(
                _ => throw exception,
                NullLogger<ExceptionHandlingMiddleware>.Instance);

            await middleware.InvokeAsync(context);

            bodyStream.Position = 0;
            ErrorResponse body = (await JsonSerializer.DeserializeAsync<ErrorResponse>(bodyStream, JsonDefaults.CamelCase))!;
            return (context.Response.StatusCode, body);
        }

        private sealed class StartedResponseFeature : IHttpResponseFeature
        {
            private readonly Stream _body;

            public StartedResponseFeature(Stream body)
            {
                _body = body;
            }

            public int StatusCode { get; set; } = StatusCodes.Status200OK;
            public string? ReasonPhrase { get; set; }
            public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
            public Stream Body { get => _body; set => _ = value; }
            public bool HasStarted => true;

            public void OnStarting(Func<object, Task> callback, object state)
            {
            }

            public void OnCompleted(Func<object, Task> callback, object state)
            {
            }
        }
    }
}
