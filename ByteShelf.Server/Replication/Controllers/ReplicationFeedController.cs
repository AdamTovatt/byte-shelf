using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Shared.RateLimiting;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ByteShelf.Server.Replication.Controllers
{
    /// <summary>
    /// What a primary serves its replicas. Every action but <see cref="Accept"/>, which a pairing code's
    /// secret authorises, needs a replica's credential. The log carries users' password hashes and keys'
    /// secret hashes, so nothing here is open to anybody else. The credentialed actions are outside the
    /// shared rate limits: a replica taking a copy asks for every object one by one, and its credential
    /// has 256 random bits, so nobody without it gets past the first lookup.
    /// </summary>
    [ApiController]
    [Route("api/replication/feed")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class ReplicationFeedController : ControllerBase
    {
        private readonly ReplicationPairing _pairing;
        private readonly IReplicationFeed _feed;

        public ReplicationFeedController(ReplicationPairing pairing, IReplicationFeed feed)
        {
            _pairing = pairing;
            _feed = feed;
        }

        /// <summary>Pairs the replica presenting a code's secret; 404 when the code has been used or has run out.</summary>
        [HttpPost("accept")]
        [EnableRateLimiting(RateLimitPolicies.VeryStrict)]
        public async Task<IActionResult> Accept([FromBody] AcceptPairingRequest request)
        {
            return Ok(await _pairing.AcceptAsync(request));
        }

        /// <summary>
        /// The change log after <c>after</c>, which is how far the replica has applied it; <c>fingerprint</c>
        /// identifies the entry it applied last.
        /// </summary>
        [HttpGet("changes")]
        [DisableRateLimiting]
        [ReplicaAuthenticated]
        public async Task<IActionResult> GetChanges([FromQuery] long after, [FromQuery] string? fingerprint, [FromQuery] int limit = IReplicationFeed.MaxPageSize)
        {
            return Ok(await _feed.GetChangesAsync(ReplicaAuthenticatedAttribute.GetReplica(HttpContext), after, fingerprint, limit));
        }

        /// <summary>A page of a whole copy: the newest state of every entity, after <c>after</c>.</summary>
        [HttpGet("copy")]
        [DisableRateLimiting]
        [ReplicaAuthenticated]
        public async Task<IActionResult> GetCopy([FromQuery] long after, [FromQuery] int limit = IReplicationFeed.MaxPageSize)
        {
            return Ok(await _feed.GetCopyAsync(after, limit));
        }

        /// <summary>The bytes of a file's object, live or in the trash, by the file's id.</summary>
        [HttpGet("files/{id:guid}/object")]
        [DisableRateLimiting]
        [ReplicaAuthenticated]
        public async Task<IActionResult> GetObject(Guid id)
        {
            string path = await _feed.GetObjectPathAsync(id)
                ?? throw new NotFoundException("There is no such file, or it has been purged.");

            return PhysicalFile(path, "application/octet-stream");
        }

        /// <summary>The replica has stopped following this server, as when it was promoted.</summary>
        [HttpDelete("pairing")]
        [DisableRateLimiting]
        [ReplicaAuthenticated]
        public async Task<IActionResult> Leave()
        {
            await _pairing.ForgetReplicaAsync(ReplicaAuthenticatedAttribute.GetReplica(HttpContext));
            return NoContent();
        }
    }
}
