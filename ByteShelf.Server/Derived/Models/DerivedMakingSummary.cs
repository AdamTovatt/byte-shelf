namespace ByteShelf.Server.Derived.Models
{
    /// <summary>What one pass of the job <c>derived.making</c> did.</summary>
    /// <param name="Contents">How many contents plugins ran for.</param>
    public record DerivedMakingSummary(int Contents);
}
