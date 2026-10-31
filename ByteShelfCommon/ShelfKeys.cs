namespace ByteShelfCommon
{
    /// <summary>How a program presents a shelf key.</summary>
    public static class ShelfKeys
    {
        /// <summary>The request header carrying a key's secret, on every request a program makes.</summary>
        public const string HeaderName = "X-Api-Key";

        /// <summary>How every key's secret starts, so a leaked one is easy to recognize and search for.</summary>
        public const string SecretPrefix = "bsk_";

        /// <summary>The longest name a key can have.</summary>
        public const int MaxNameLength = 100;
    }
}
