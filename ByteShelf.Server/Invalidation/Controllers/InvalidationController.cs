using System.Text.Json;
using ByteShelf.Server.Invalidation.Models;
using ByteShelf.Server.Invalidation.Services;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Json;
using ByteShelf.Server.Shared.RateLimiting;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Invalidation.Controllers
{
    [ApiController]
    [Route("api/invalidation")]
    public class InvalidationController : ControllerBase
    {
        internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(25);

        private readonly IInvalidationBroadcaster _broadcaster;
        private readonly InvalidationAudience _audience;
        private readonly TimeProvider _timeProvider;
        private readonly IHostApplicationLifetime _lifetime;

        public InvalidationController(IInvalidationBroadcaster broadcaster, InvalidationAudience audience, TimeProvider timeProvider, IHostApplicationLifetime lifetime)
        {
            _broadcaster = broadcaster;
            _audience = audience;
            _timeProvider = timeProvider;
            _lifetime = lifetime;
        }

        /// <summary>
        /// Holds a <c>text/event-stream</c> open and writes each invalidation the caller may hear of as it is
        /// published, with a comment heartbeat every 25 seconds. A caller may hold a limited number of
        /// streams at once. The stream ends once the caller's account is gone, and as the server begins to stop:
        /// Kestrel cancels no request when it stops but waits for each to end, so an open stream would hold the
        /// stop until the host's shutdown timeout.
        /// </summary>
        [HttpGet("stream")]
        [Authorize]
        public async Task Stream(CancellationToken requestAborted)
        {
            Guid userId = HttpContext.GetUserGuid();
            using IInvalidationSubscription? subscription = _broadcaster.Subscribe(RateLimitPartitionKey.Resolve(HttpContext));
            if (subscription == null)
            {
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await Response.WriteAsJsonAsync(
                    new ErrorResponse { Message = "Too many live-update streams are open for this user." },
                    JsonDefaults.CamelCase,
                    requestAborted);
                return;
            }

            // A short refusal is no stream to end as the server stops; the stream is.
            using CancellationTokenSource streamEnd = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, _lifetime.ApplicationStopping);
            CancellationToken cancellationToken = streamEnd.Token;

            Response.Headers.Append("Content-Type", "text/event-stream; charset=utf-8");
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Accel-Buffering", "no");

            try
            {
                // Sends the headers now, so the client knows at once that the stream is open rather
                // than at the first event or heartbeat.
                await Response.Body.FlushAsync(cancellationToken);

                using PeriodicTimer heartbeatTimer = new PeriodicTimer(HeartbeatInterval, _timeProvider);
                Task<bool> heartbeatTask = heartbeatTimer.WaitForNextTickAsync(cancellationToken).AsTask();
                Task<bool> readTask = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();

                while (!cancellationToken.IsCancellationRequested)
                {
                    Task completed = await Task.WhenAny(readTask, heartbeatTask);

                    if (completed == readTask)
                    {
                        if (!await readTask)
                        {
                            break;
                        }

                        List<InvalidationEvent> published = new List<InvalidationEvent>();
                        while (subscription.Reader.TryRead(out InvalidationEvent? invalidationEvent))
                        {
                            published.Add(invalidationEvent);
                        }

                        IReadOnlyList<InvalidationEvent>? audible = await _audience.FilterAsync(userId, published);
                        if (audible == null)
                        {
                            break;
                        }

                        foreach (InvalidationEvent invalidationEvent in audible)
                        {
                            string json = JsonSerializer.Serialize(invalidationEvent, JsonDefaults.CamelCase);
                            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                        }

                        await Response.Body.FlushAsync(cancellationToken);
                        readTask = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
                    }
                    else
                    {
                        if (!await heartbeatTask)
                        {
                            break;
                        }

                        await Response.WriteAsync(": heartbeat\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                        heartbeatTask = heartbeatTimer.WaitForNextTickAsync(cancellationToken).AsTask();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
