namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Tests that change the process's working directory or environment, which every other test shares: run alone.</summary>
    [CollectionDefinition(nameof(ProcessStateCollection), DisableParallelization = true)]
    public sealed class ProcessStateCollection
    {
    }
}
