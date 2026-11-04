using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Shared.IO;
using ByteShelf.Server.Shared.Json;

namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// A replica's calls to its primary's replication feed. Every failure, whether the primary could not
    /// be reached or answered with something unusable, is a <see cref="PrimaryFailedException"/> worded
    /// here: nothing the other server wrote is shown to anybody. A call is given up once nothing has
    /// arrived for <see cref="ReplicationOptions.IdleTimeout"/>, however long its answer takes as a whole.
    /// </summary>
    public sealed class PrimaryClient
    {
        /// <summary>The name of the <see cref="HttpClient"/> these calls use; it follows no redirects.</summary>
        public const string HttpClientName = "replication-primary";

        private readonly HttpClient _http;
        private readonly TimeSpan _idleTimeout;
        private readonly TimeProvider _timeProvider;

        public PrimaryClient(IHttpClientFactory httpClientFactory, ReplicationOptions options, TimeProvider timeProvider)
        {
            _http = httpClientFactory.CreateClient(HttpClientName);
            _idleTimeout = options.IdleTimeout;
            _timeProvider = timeProvider;
        }

        /// <summary>Pairs with the primary using a code's secret; a used or expired code is refused.</summary>
        public async Task<AcceptPairingResponse> AcceptAsync(string primaryUrl, AcceptPairingRequest request, CancellationToken cancellationToken)
        {
            using HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, FeedUri(primaryUrl, "accept"))
            {
                Content = JsonContent.Create(request, options: JsonDefaults.CamelCase),
            };

            return await ExchangeAsync(message, primaryUrl, async (response, body, token) =>
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new ConflictException("The primary did not accept the code: it has been used or has run out. Make a new one there.");
                }

                return await ReadJsonAsync<AcceptPairingResponse>(response, body, primaryUrl, token);
            }, cancellationToken);
        }

        /// <summary>
        /// The primary's change log after <paramref name="afterSequence"/>; <paramref name="afterFingerprint"/>
        /// identifies the entry applied last, when known.
        /// </summary>
        public async Task<ChangeFeedPage> GetChangesAsync(ReplicationSource source, long afterSequence, string? afterFingerprint, int limit, CancellationToken cancellationToken)
        {
            string query = $"changes?after={afterSequence.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}";
            if (afterFingerprint != null)
            {
                query += "&fingerprint=" + Uri.EscapeDataString(afterFingerprint);
            }

            return await GetPageAsync(source, query, cancellationToken);
        }

        /// <summary>A page of a whole copy: the newest entry of each entity, its state or its removal, after <paramref name="afterSequence"/>.</summary>
        public async Task<ChangeFeedPage> GetCopyAsync(ReplicationSource source, long afterSequence, int limit, CancellationToken cancellationToken)
        {
            return await GetPageAsync(source, $"copy?after={afterSequence.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
        }

        /// <summary>
        /// Copies the bytes of file <paramref name="fileId"/>, as <paramref name="sha256"/>, into
        /// <paramref name="destination"/>; checking their hash is the caller's. False when the primary's
        /// file no longer holds them: purged, or its contents swapped since.
        /// </summary>
        public async Task<bool> DownloadObjectAsync(ReplicationSource source, Guid fileId, string sha256, Stream destination, CancellationToken cancellationToken)
        {
            using HttpRequestMessage message = Authorized(source, HttpMethod.Get, $"files/{fileId:D}/object?sha256={Uri.EscapeDataString(sha256)}");
            return await ExchangeAsync(message, source.PrimaryUrl, async (response, body, token) =>
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }

                EnsureUsable(response, source.PrimaryUrl);
                await body.CopyToAsync(destination, token);
                return true;
            }, cancellationToken);
        }

        /// <summary>Tells the primary this replica has stopped following it; false when it could not be told.</summary>
        public async Task<bool> LeaveAsync(ReplicationSource source, CancellationToken cancellationToken)
        {
            try
            {
                using HttpRequestMessage message = Authorized(source, HttpMethod.Delete, "pairing");

                // Unauthorized: the primary already has no such replica, which is what was asked.
                return await ExchangeAsync(
                    message,
                    source.PrimaryUrl,
                    (response, body, token) => Task.FromResult(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized),
                    cancellationToken);
            }
            catch (PrimaryFailedException)
            {
                return false;
            }
        }

        private async Task<ChangeFeedPage> GetPageAsync(ReplicationSource source, string pathAndQuery, CancellationToken cancellationToken)
        {
            using HttpRequestMessage message = Authorized(source, HttpMethod.Get, pathAndQuery);
            ChangeFeedPage page = await ExchangeAsync(
                message,
                source.PrimaryUrl,
                (response, body, token) => ReadJsonAsync<ChangeFeedPage>(response, body, source.PrimaryUrl, token),
                cancellationToken);

            if (!string.Equals(page.InstanceId, source.PrimaryInstanceId, StringComparison.Ordinal))
            {
                throw new PrimaryFailedException($"The server at {source.PrimaryUrl} is no longer the primary this replica was paired with.");
            }

            return page;
        }

        private static HttpRequestMessage Authorized(ReplicationSource source, HttpMethod method, string pathAndQuery)
        {
            HttpRequestMessage message = new HttpRequestMessage(method, FeedUri(source.PrimaryUrl, pathAndQuery));
            message.Headers.Add(ReplicaCredentials.HeaderName, source.Credential);
            return message;
        }

        private static Uri FeedUri(string primaryUrl, string pathAndQuery)
        {
            return new Uri(new Uri(primaryUrl.TrimEnd('/') + "/"), "api/replication/feed/" + pathAndQuery);
        }

        /// <summary>
        /// Sends <paramref name="message"/> and hands the answer and its body to <paramref name="handle"/>,
        /// all under one timeout that restarts whenever part of the answer arrives.
        /// </summary>
        private async Task<T> ExchangeAsync<T>(
            HttpRequestMessage message,
            string primaryUrl,
            Func<HttpResponseMessage, Stream, CancellationToken, Task<T>> handle,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource idle = new CancellationTokenSource(_idleTimeout, _timeProvider);
            using CancellationTokenSource call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idle.Token);

            try
            {
                using HttpResponseMessage response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, call.Token);
                await using Stream body = new IdleTimeoutStream(await response.Content.ReadAsStreamAsync(call.Token), () => idle.CancelAfter(_idleTimeout));
                return await handle(response, body, call.Token);
            }
            catch (HttpRequestException exception)
            {
                throw new PrimaryFailedException($"The primary at {primaryUrl} could not be reached.", exception);
            }
            catch (HttpIOException exception)
            {
                // Not every IOException: one writing to this server's own disk is its own fault.
                throw new PrimaryFailedException($"The primary at {primaryUrl} stopped answering part way.", exception);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new PrimaryFailedException($"The primary at {primaryUrl} sent nothing for {_idleTimeout.TotalSeconds:0} seconds.", exception);
            }
        }

        private static void EnsureUsable(HttpResponseMessage response, string primaryUrl)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            string reason = (int)response.StatusCode switch
            {
                401 => "no longer recognises this replica; pair it again with a new code",
                409 => "is itself a replica, or refused the request",
                429 => "is limiting how often it is asked; try again shortly",
                >= 300 and < 400 => "redirected the request elsewhere; its APP_BASE_URL must be the address it answers at",
                int status => $"answered {status}",
            };

            throw new PrimaryFailedException($"The primary at {primaryUrl} {reason}.");
        }

        private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, Stream body, string primaryUrl, CancellationToken cancellationToken)
            where T : class
        {
            EnsureUsable(response, primaryUrl);

            try
            {
                return await JsonSerializer.DeserializeAsync<T>(body, JsonDefaults.CamelCase, cancellationToken)
                    ?? throw new PrimaryFailedException($"The primary at {primaryUrl} answered without a body.");
            }
            catch (JsonException exception)
            {
                throw new PrimaryFailedException($"The primary at {primaryUrl} answered with something that is not a replication feed.", exception);
            }
        }
    }
}
