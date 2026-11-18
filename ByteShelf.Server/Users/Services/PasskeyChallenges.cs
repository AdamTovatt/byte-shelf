using System.Security.Cryptography;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// The challenges of passkey ceremonies under way, in memory, each under a random ceremony id the
    /// browser hands back with its answer. A challenge is taken once, whether the ceremony then passes or
    /// fails, and lasts <see cref="Lifetime"/>. One for adding a passkey is bound to the user who began it;
    /// one for signing in is bound to no one, as no one is known yet. Ceremonies are short, so the few
    /// under way at a restart are simply begun again.
    /// </summary>
    /// <remarks>
    /// Anyone may begin a sign-in, so at most <see cref="Capacity"/> are kept: past it the oldest goes. A
    /// flood then costs ceremonies under way their challenge, never the server its memory.
    /// </remarks>
    public sealed class PasskeyChallenges
    {
        /// <summary>
        /// How long a ceremony may take, which is also the timeout its options give the browser. Long enough
        /// for a sign-in page left open with its passkey suggestions showing.
        /// </summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

        public const int Capacity = 10_000;

        private readonly TimeProvider _timeProvider;
        private readonly Dictionary<string, Pending> _pending = new Dictionary<string, Pending>(StringComparer.Ordinal);

        /// <summary>Ceremony ids oldest first; one may already be taken, in which case it is passed over.</summary>
        private readonly Queue<string> _order = new Queue<string>();

        private readonly object _lock = new object();

        public PasskeyChallenges(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        /// <summary>Keeps <paramref name="challenge"/> for a ceremony of <paramref name="userId"/>, or of no one when null, and answers its id.</summary>
        public string Keep(string challenge, Guid? userId)
        {
            string ceremonyId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            DateTimeOffset now = _timeProvider.GetUtcNow();

            lock (_lock)
            {
                while (_order.TryPeek(out string? oldest)
                    && (!_pending.TryGetValue(oldest, out Pending? pending) || pending.ExpiresAt <= now || _pending.Count >= Capacity))
                {
                    _pending.Remove(_order.Dequeue());
                }

                _pending[ceremonyId] = new Pending(challenge, userId, now + Lifetime);
                _order.Enqueue(ceremonyId);
            }

            return ceremonyId;
        }

        /// <summary>
        /// Takes the challenge of <paramref name="ceremonyId"/> for good; null when there is none, it ran
        /// out, or the ceremony is not <paramref name="userId"/>'s (of no one, when null).
        /// </summary>
        public string? Take(string ceremonyId, Guid? userId)
        {
            Pending? pending;
            lock (_lock)
            {
                if (!_pending.Remove(ceremonyId, out pending))
                {
                    return null;
                }
            }

            return pending.UserId == userId && pending.ExpiresAt > _timeProvider.GetUtcNow() ? pending.Challenge : null;
        }

        private sealed record Pending(string Challenge, Guid? UserId, DateTimeOffset ExpiresAt);
    }
}
