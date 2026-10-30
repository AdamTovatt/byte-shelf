using System.Text.Json;
using ByteShelf.Server.Invalidation.Models;
using ByteShelf.Server.Invalidation.Services;
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
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(25);

        private readonly IInvalidationBroadcaster _broadcaster;

        public InvalidationController(IInvalidationBroadcaster broadcaster)
        {
            _broadcaster = broadcaster;
        }

        /// <summary>
        /// Holds a <c>text/event-stream</c> open and writes each invalidation as it is published, with a
        /// comment heartbeat every 25 seconds. A caller may hold a limited number of streams at once.
        /// </summary>
        [HttpGet("stream")]
        [Authorize]
        public async Task Stream(CancellationToken cancellationToken)
        {
            using IInvalidationSubscription? subscription = _broadcaster.Subscribe(RateLimitPartitionKey.Resolve(HttpContext));
            if (subscription == null)
            {
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await Response.WriteAsJsonAsync(
                    new ErrorResponse { Message = "Too many live-update streams are open for this user." },
                    JsonDefaults.CamelCase,
                    cancellationToken);
                return;
            }

            Response.Headers.Append("Content-Type", "text/event-stream; charset=utf-8");
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Accel-Buffering", "no");

            try
            {
                // Sends the headers now, so the client knows at once that the stream is open rather
                // than at the first event or heartbeat.
                await Response.Body.FlushAsync(cancellationToken);

                using PeriodicTimer heartbeatTimer = new PeriodicTimer(HeartbeatInterval);
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

                        while (subscription.Reader.TryRead(out InvalidationEvent? invalidationEvent))
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
