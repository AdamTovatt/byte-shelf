using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// Batches of moves and descriptions, as <see cref="EntryBatch"/> applies them: each item through the
    /// one-entry services, in its own transaction with its own change-log entry, in the order given. An
    /// item refused (anything the API answers below 500, <see cref="Refusals"/>) is that item's result;
    /// a fault ends the batch, leaving the items before it done.
    /// </summary>
    /// <remarks>Named after Common's rules as <see cref="DownloadLinks"/> is after <see cref="DownloadLink"/>.</remarks>
    public class EntryBatches
    {
        private readonly IFileService _fileService;
        private readonly IDirectoryService _directoryService;

        public EntryBatches(IFileService fileService, IDirectoryService directoryService)
        {
            _fileService = fileService;
            _directoryService = directoryService;
        }

        /// <summary>Applies each move in turn, a file's or a directory's, whichever its path names.</summary>
        /// <exception cref="ArgumentException">The request is not allowed (<see cref="MoveEntriesRequest.Normalize"/>).</exception>
        /// <exception cref="OperationCanceledException">The caller went away; the items before it stay done.</exception>
        public Task<IReadOnlyList<EntryMoveResult>> MoveAsync(Shelf shelf, MoveEntriesRequest request, CancellationToken cancellationToken)
        {
            return ApplyAsync(request.Normalize(), move => MoveOneAsync(shelf, move), EntryMoveResult.Refused, cancellationToken);
        }

        /// <summary>Sets each description in turn.</summary>
        /// <exception cref="ArgumentException">The request is not allowed (<see cref="SetDescriptionsRequest.Normalize"/>).</exception>
        /// <exception cref="OperationCanceledException">The caller went away; the items before it stay done.</exception>
        public Task<IReadOnlyList<DescriptionResult>> SetDescriptionsAsync(Shelf shelf, SetDescriptionsRequest request, CancellationToken cancellationToken)
        {
            return ApplyAsync(
                request.Normalize(),
                async change => DescriptionResult.Described(await _fileService.SetDescriptionAsync(shelf, EntryReference.ForPath(change.Path), change.Description)),
                DescriptionResult.Refused,
                cancellationToken);
        }

        /// <summary>Applies each item in order, a refusal its result, stopping between items when the caller goes away.</summary>
        private static async Task<IReadOnlyList<TResult>> ApplyAsync<TItem, TResult>(
            IReadOnlyList<TItem> items,
            Func<TItem, Task<TResult>> apply,
            Func<TItem, ErrorResponse, TResult> refused,
            CancellationToken cancellationToken)
        {
            List<TResult> results = new List<TResult>(items.Count);
            foreach (TItem item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    results.Add(await apply(item));
                }
                catch (Exception exception) when (Refusals.Of(exception) is ErrorResponse refusal)
                {
                    results.Add(refused(item, refusal));
                }
            }

            return results;
        }

        private async Task<EntryMoveResult> MoveOneAsync(Shelf shelf, EntryMove move)
        {
            EntryReference entry = EntryReference.ForPath(move.Path);
            try
            {
                return EntryMoveResult.Moved(move, await _fileService.MoveFileAsync(shelf, entry, move.ToPath));
            }
            catch (NotFoundException)
            {
                // A directory, or nothing: a file move finds nothing only at its own path.
            }

            try
            {
                return EntryMoveResult.Moved(move, await _directoryService.MoveAsync(shelf, entry, move.ToPath));
            }
            catch (NotFoundException)
            {
                throw new NotFoundException($"Neither a file nor a directory is at '{move.Path}' on this shelf.");
            }
        }
    }
}
