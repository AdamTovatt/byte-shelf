namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// Tests that set the process's local time zone (<see cref="LocalTimeZone.Set"/>), which every other
    /// test reads, so they run alone.
    /// </summary>
    [CollectionDefinition(nameof(LocalTimeZoneCollection), DisableParallelization = true)]
    public class LocalTimeZoneCollection
    {
    }

    internal static class LocalTimeZone
    {
        /// <summary>
        /// Makes <paramref name="ianaId"/> the process's local time zone until the result is disposed, as
        /// a server whose clock is set to it would have. Only for tests in <see cref="LocalTimeZoneCollection"/>.
        /// </summary>
        public static IDisposable Set(string ianaId)
        {
            string? before = Environment.GetEnvironmentVariable("TZ");
            Environment.SetEnvironmentVariable("TZ", ianaId);
            TimeZoneInfo.ClearCachedData();
            return new Restore(before);
        }

        private sealed class Restore : IDisposable
        {
            private readonly string? _before;

            public Restore(string? before)
            {
                _before = before;
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable("TZ", _before);
                TimeZoneInfo.ClearCachedData();
            }
        }
    }
}
