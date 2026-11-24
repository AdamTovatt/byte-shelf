namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    /// <summary>Every pair of two lists, as cases.</summary>
    public sealed class MatrixTheoryData<TFirst, TSecond> : TheoryData<TFirst, TSecond>
    {
        public MatrixTheoryData(IEnumerable<TFirst> first, IReadOnlyList<TSecond> second)
        {
            foreach (TFirst one in first)
            {
                foreach (TSecond other in second)
                {
                    Add(one, other);
                }
            }
        }
    }
}
