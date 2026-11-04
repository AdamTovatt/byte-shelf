namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// Shared fixed instants for deterministic-time tests, so every suite anchors its
    /// <c>FakeTimeProvider</c> to the same well-known "now" instead of redeclaring it.
    /// </summary>
    public static class TestInstants
    {
        public static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }
}
