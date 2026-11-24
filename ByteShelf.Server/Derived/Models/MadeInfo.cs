namespace ByteShelf.Server.Derived.Models
{
    /// <summary>What is known of a derived file that was made.</summary>
    /// <param name="MediaType">Its media type.</param>
    /// <param name="Original">What it was made from; null when its maker says nothing of it.</param>
    public sealed record MadeInfo(string MediaType, DerivedOriginal? Original);
}
