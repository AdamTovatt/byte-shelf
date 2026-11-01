using ByteShelf.Server.Replication.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ByteShelf.Server.Replication.Controllers
{
    /// <summary>Replication for this server's admins: pairing codes and replicas on a primary, pairing and promoting a replica.</summary>
    [ApiController]
    [Route("api/replication")]
    [Authorize(Roles = UserRoles.Admin)]
    public class ReplicationController : ControllerBase
    {
        private readonly ReplicationPairing _pairing;

        public ReplicationController(ReplicationPairing pairing)
        {
            _pairing = pairing;
        }

        /// <summary>The replicas paired with this server and how far behind each is, or the primary this replica follows.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(ReplicationStatus), 200)]
        public async Task<IActionResult> GetStatus()
        {
            return Ok(await _pairing.GetStatusAsync());
        }

        /// <summary>A pairing code for one replica, usable once for <see cref="ReplicationOptions.CodeLifetime"/>. Refused on a replica.</summary>
        [HttpPost("codes")]
        [ProducesResponseType(typeof(PairingCodeResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> CreateCode()
        {
            return Ok(await _pairing.CreateCodeAsync());
        }

        /// <summary>Unpairs a replica: it can no longer read this server's log.</summary>
        [HttpDelete("replicas/{id:guid}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> RemoveReplica(Guid id)
        {
            await _pairing.RemoveReplicaAsync(id);
            return NoContent();
        }

        /// <summary>
        /// Makes this server a replica of the primary that made the code. Only a server without shelves
        /// or replicas may; it takes the primary's shelves and users, so its own users are replaced.
        /// </summary>
        [HttpPost("pair")]
        [ProducesResponseType(typeof(ReplicationStatus), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        [ProducesResponseType(typeof(ErrorResponse), 502)]
        public async Task<IActionResult> Pair([FromBody] PairWithPrimaryRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _pairing.PairAsync(request, cancellationToken));
        }

        /// <summary>
        /// Stops following the primary, so this server takes changes itself. Answers whether the primary
        /// heard; when it did not, the replica stays listed there until it is removed.
        /// </summary>
        [HttpPost("promote")]
        [AllowedOnReplica]
        [ProducesResponseType(typeof(PromoteResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> Promote(CancellationToken cancellationToken)
        {
            bool primaryTold = await _pairing.PromoteAsync(cancellationToken);
            return Ok(new PromoteResponse { PrimaryTold = primaryTold });
        }
    }
}
