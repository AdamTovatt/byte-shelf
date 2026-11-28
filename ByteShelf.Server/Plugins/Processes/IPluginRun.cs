using System.Text.Json;

namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>Something a plugin is to do over contents, and what its answer comes to.</summary>
    internal interface IPluginRun<T>
    {
        PluginTarget Plugin { get; }

        /// <summary>The contents.</summary>
        string Sha256 { get; }

        /// <summary>What to do: the type of a <see cref="ProcessWork"/>.</summary>
        Type Work { get; }

        /// <summary>The answer when there is nothing to do, as when no plain file holds the contents.</summary>
        T Nothing { get; }

        /// <summary>What the plugin needs to know beside the contents; null when there is nothing to do. Asked with the contents' lock held.</summary>
        /// <param name="mediaTypes">What plain files hold the contents as; one at least.</param>
        JsonElement? Prepare(IReadOnlyList<string> mediaTypes);

        /// <summary>Told that a loaded process of the plugin was given the run, which says the plugin can run; it may be told again, as a run is done anew.</summary>
        void Began();

        /// <summary>Takes the result, with whatever files it wants from <paramref name="folder"/>, which is emptied next.</summary>
        Task<T> TakeAsync(PluginRunResult result, WorkFolder folder, CancellationToken cancellationToken);
    }
}
