using ByteShelfCommon;

namespace ByteShelfClient.InMemory
{
    /// <summary>Everything an <see cref="InMemoryByteShelfClient"/> holds, behind one lock.</summary>
    internal sealed class InMemoryStore
    {
        public InMemoryStore(TimeProvider timeProvider, TimeSpan restoreWindow)
        {
            TimeProvider = timeProvider;
            RestoreWindow = restoreWindow;
        }

        /// <summary>Held around every read and change, so each call sees and leaves a consistent state.</summary>
        public object Gate { get; } = new object();

        public TimeProvider TimeProvider { get; }

        public TimeSpan RestoreWindow { get; }

        public List<InMemoryShelf> Shelves { get; } = new List<InMemoryShelf>();

        public List<UserRow> Users { get; } = new List<UserRow>();

        public DateTimeOffset Now => TimeProvider.GetUtcNow();

        /// <summary>
        /// Runs <paramref name="operation"/> under <see cref="Gate"/> and answers the task an HTTP call
        /// would: a refusal and a cancellation come through it rather than being thrown from the call.
        /// </summary>
        public Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            try
            {
                lock (Gate)
                {
                    return Task.FromResult(operation());
                }
            }
            catch (Exception exception)
            {
                return Task.FromException<T>(exception);
            }
        }

        /// <inheritdoc cref="RunAsync{T}(Func{T}, CancellationToken)"/>
        public Task RunAsync(Action operation, CancellationToken cancellationToken)
        {
            return RunAsync(
                () =>
                {
                    operation();
                    return true;
                },
                cancellationToken);
        }

        /// <summary>The shelf called <paramref name="name"/>, with anything past its restore window purged.</summary>
        public InMemoryShelf GetShelf(string name)
        {
            InMemoryShelf shelf = Shelves.FirstOrDefault(candidate => candidate.Name == name)
                ?? throw InMemoryErrors.NotFound($"There is no shelf '{name}'.");

            DateTimeOffset cutoff = Now - RestoreWindow;
            shelf.Files.RemoveAll(file => file.DeletedAt < cutoff);
            shelf.Directories.RemoveAll(directory => directory.DeletedAt < cutoff);
            return shelf;
        }

        public UserRow GetUser(Guid userId)
        {
            return Users.FirstOrDefault(user => user.Id == userId)
                ?? throw InMemoryErrors.NotFound($"There is no user {userId}.");
        }

        internal sealed class UserRow
        {
            public required Guid Id { get; init; }
            public required string Email { get; init; }
            public required string DisplayName { get; init; }
            public required IReadOnlyList<string> Roles { get; set; }
            public required string Password { get; set; }

            public UserInfo ToInfo()
            {
                return new UserInfo { Id = Id, Email = Email, DisplayName = DisplayName, Roles = Roles.ToList() };
            }
        }
    }
}
