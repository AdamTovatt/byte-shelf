using ByteShelf.Server.Shared;
using EasyReasy.EnvironmentVariables;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>The storage domain's tunables, read once at startup.</summary>
    public class StorageOptions
    {
        public const int DefaultRestoreWindowDays = 30;

        /// <summary>The most days <c>BYTESHELF_RESTORE_WINDOW_DAYS</c> may hold: ten years.</summary>
        public const int MaxRestoreWindowDays = 3650;

        /// <summary>How long a deletion can be restored before it is purged.</summary>
        public TimeSpan RestoreWindow { get; init; } = TimeSpan.FromDays(DefaultRestoreWindowDays);

        /// <summary>How long a resumable upload may go without a chunk before it is removed.</summary>
        public TimeSpan AbandonedUploadAge { get; init; } = TimeSpan.FromHours(24);

        /// <summary>How often the purge and sweep run.</summary>
        public TimeSpan MaintenanceInterval { get; init; } = TimeSpan.FromHours(1);

        /// <summary>The options with <see cref="RestoreWindow"/> read from the environment.</summary>
        /// <exception cref="InvalidOperationException"><c>BYTESHELF_RESTORE_WINDOW_DAYS</c> is not a whole number of days from 1 to <see cref="MaxRestoreWindowDays"/>.</exception>
        public static StorageOptions FromEnvironment()
        {
            return new StorageOptions { RestoreWindow = RestoreWindowFrom(EnvironmentVariables.RestoreWindowDays.GetValueOrDefault()) };
        }

        /// <summary><see cref="RestoreWindow"/> as <c>BYTESHELF_RESTORE_WINDOW_DAYS</c> gives it: null (unset or blank) for the default.</summary>
        /// <exception cref="InvalidOperationException">It is not a whole number of days from 1 to <see cref="MaxRestoreWindowDays"/>.</exception>
        public static TimeSpan RestoreWindowFrom(string? days)
        {
            int? parsed = WholeNumberSetting.Parse(EnvironmentVariables.RestoreWindowDays, days, "days", 1, MaxRestoreWindowDays);
            return TimeSpan.FromDays(parsed ?? DefaultRestoreWindowDays);
        }
    }
}
