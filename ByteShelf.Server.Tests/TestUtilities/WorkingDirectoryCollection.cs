namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Tests that change the process's working directory, which every other test shares: run alone.</summary>
    [CollectionDefinition(nameof(WorkingDirectoryCollection), DisableParallelization = true)]
    public sealed class WorkingDirectoryCollection
    {
    }
}
