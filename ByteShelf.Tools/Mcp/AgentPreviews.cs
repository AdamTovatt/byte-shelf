using ByteShelfClient;
using ByteShelfCommon;

namespace ByteShelf.Tools.Mcp
{
    /// <summary>What a plugin of the server's makes of a file for an agent to see, waited for while it is made.</summary>
    internal static class AgentPreviews
    {
        /// <summary>How long a tool waits for one; the server may be making others first.</summary>
        internal static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

        /// <summary>How long it waits before it asks again, beside what the server itself waits.</summary>
        private static readonly TimeSpan AskInterval = TimeSpan.FromSeconds(1);

        /// <summary>The preview's bytes, at most <see cref="ByteShelfMcpTools.MaxInlineImageBytes"/> of them and one more; null when there is none.</summary>
        /// <exception cref="TimeoutException">It is still being made.</exception>
        public static async Task<byte[]?> ReadAsync(FileSource source, ShelfFile file, TimeProvider timeProvider, CancellationToken cancellationToken)
        {
            using CancellationTokenSource limit = new CancellationTokenSource(WaitLimit, timeProvider);
            using CancellationTokenSource either = CancellationTokenSource.CreateLinkedTokenSource(limit.Token, cancellationToken);
            try
            {
                while (true)
                {
                    try
                    {
                        await using DerivedFileContent preview = await source.OpenDerivedAsync(file, DerivedKind.Agent, either.Token);
                        return await ReadAsync(preview.Content, either.Token);
                    }
                    catch (ByteShelfException exception) when (exception.IsNotFound)
                    {
                        return null;
                    }
                    catch (ByteShelfException exception) when (exception.IsNotReady)
                    {
                        await Task.Delay(AskInterval, timeProvider, either.Token);
                    }
                }
            }
            catch (OperationCanceledException) when (limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The preview is still being made.");
            }
        }

        private static async Task<byte[]> ReadAsync(Stream content, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[ByteShelfMcpTools.MaxInlineImageBytes + 1];
            return buffer[..await content.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken)];
        }
    }
}
