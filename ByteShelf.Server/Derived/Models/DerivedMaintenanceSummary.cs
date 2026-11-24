namespace ByteShelf.Server.Derived.Models
{
    /// <summary>What one pass of the job <c>derived.maintenance</c> removed, and how many sizes it counted again.</summary>
    /// <param name="Unheld">Contents no plain file held any more, that something was derived from or a plugin's process had read.</param>
    /// <param name="Orphaned">Derived files nothing installed makes, or that a crash left part of.</param>
    /// <param name="Trimmed">Derived files over the limit of their kind.</param>
    /// <param name="Recounted">Derived files whose size written for quota was not what the folder holds, after the rest.</param>
    public record DerivedMaintenanceSummary(int Unheld, int Orphaned, int Trimmed, int Recounted);
}
