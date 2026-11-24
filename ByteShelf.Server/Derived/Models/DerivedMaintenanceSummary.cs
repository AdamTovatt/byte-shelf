namespace ByteShelf.Server.Derived.Models
{
    /// <summary>What one pass of the job <c>derived.maintenance</c> removed.</summary>
    /// <param name="Unheld">Contents no plain file held any more.</param>
    /// <param name="Orphaned">Derived files nothing installed makes, or that a crash left part of.</param>
    /// <param name="Trimmed">Derived files over the limit of their kind.</param>
    public record DerivedMaintenanceSummary(int Unheld, int Orphaned, int Trimmed);
}
