using System.Globalization;

namespace ByteShelfCommon
{
    /// <summary>What a <see cref="PluginFieldFilter"/> asks of a field.</summary>
    public abstract record PluginFieldCondition
    {
        private PluginFieldCondition()
        {
        }

        /// <summary>The kind of field it can be asked of; null for any.</summary>
        public virtual PluginFieldKind? Kind => null;

        /// <summary>Whether a field with <paramref name="value"/>, null for none, meets it, as the server's query decides. Call it on a normalized filter's.</summary>
        public abstract bool Matches(PluginFieldValue? value);

        /// <summary>The condition checked and put the way it is compared.</summary>
        /// <exception cref="ArgumentException">It is not allowed.</exception>
        internal virtual PluginFieldCondition Normalize()
        {
            return this;
        }

        /// <summary>It has a value, or has none.</summary>
        /// <param name="Present">Whether it has one.</param>
        public sealed record Has(bool Present) : PluginFieldCondition
        {
            /// <inheritdoc />
            public override bool Matches(PluginFieldValue? value)
            {
                return (value != null) == Present;
            }

            internal override string Write(string field)
            {
                return Present ? field : "!" + field;
            }
        }

        /// <summary>It is a text that contains <paramref name="Text"/>, ignoring case.</summary>
        /// <param name="Text">The text to contain.</param>
        public sealed record Contains(string Text) : PluginFieldCondition
        {
            /// <inheritdoc />
            public override PluginFieldKind? Kind => PluginFieldKind.Text;

            /// <inheritdoc />
            public override bool Matches(PluginFieldValue? value)
            {
                return value?.FoldedText()?.Contains(Text, StringComparison.Ordinal) ?? false;
            }

            internal override PluginFieldCondition Normalize()
            {
                return string.IsNullOrWhiteSpace(Text)
                    ? throw new ArgumentException("A text to contain is missing.")
                    : new Contains(FileCriteria.Fold(Text.Trim()));
            }

            internal override string Write(string field)
            {
                return $"{field}~{Text}";
            }
        }

        /// <summary>It is a number from <paramref name="Min"/> to <paramref name="Max"/>, both included.</summary>
        /// <param name="Min">The smallest; null for no least.</param>
        /// <param name="Max">The largest; null for no most.</param>
        public sealed record Between(double? Min, double? Max) : PluginFieldCondition
        {
            /// <inheritdoc />
            public override PluginFieldKind? Kind => PluginFieldKind.Number;

            /// <inheritdoc />
            public override bool Matches(PluginFieldValue? value)
            {
                return value?.Number is double number && (Min == null || number >= Min) && (Max == null || number <= Max);
            }

            internal override PluginFieldCondition Normalize()
            {
                if (!double.IsFinite(Min ?? 0) || !double.IsFinite(Max ?? 0))
                {
                    throw new ArgumentException("A number to compare with is finite.");
                }

                return RequireRange(Min == null && Max == null, Min > Max);
            }

            internal override string Write(string field)
            {
                return $"{field}={Range(Min?.ToString("R", CultureInfo.InvariantCulture), Max?.ToString("R", CultureInfo.InvariantCulture))}";
            }
        }

        /// <summary>It is an instant from <paramref name="From"/> (included) to <paramref name="Before"/> (excluded).</summary>
        /// <param name="From">The earliest; null for none.</param>
        /// <param name="Before">What it is before; null for no end.</param>
        public sealed record During(DateTimeOffset? From, DateTimeOffset? Before) : PluginFieldCondition
        {
            /// <inheritdoc />
            public override PluginFieldKind? Kind => PluginFieldKind.Date;

            /// <inheritdoc />
            public override bool Matches(PluginFieldValue? value)
            {
                return value?.Date is DateTimeOffset date && (From == null || date >= From) && (Before == null || date < Before);
            }

            internal override PluginFieldCondition Normalize()
            {
                return RequireRange(From == null && Before == null, From >= Before);
            }

            internal override string Write(string field)
            {
                return $"{field}={Range(Instant(From), Instant(Before))}";
            }
        }

        /// <summary>It is a flag that is <paramref name="Flag"/>; one that is missing is neither.</summary>
        /// <param name="Flag">What the flag is.</param>
        public sealed record Is(bool Flag) : PluginFieldCondition
        {
            /// <inheritdoc />
            public override PluginFieldKind? Kind => PluginFieldKind.Flag;

            /// <inheritdoc />
            public override bool Matches(PluginFieldValue? value)
            {
                return value?.Flag == Flag;
            }

            internal override string Write(string field)
            {
                return $"{field}={(Flag ? "true" : "false")}";
            }
        }

        /// <summary>The filter that asks this of <paramref name="field"/>, as <see cref="PluginFieldFilter"/> reads it.</summary>
        internal abstract string Write(string field);

        private static string Range(string? start, string? end)
        {
            return start + PluginFieldFilter.RangeSeparator + end;
        }

        private static string? Instant(DateTimeOffset? time)
        {
            return time?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
        }

        private PluginFieldCondition RequireRange(bool open, bool empty)
        {
            if (open)
            {
                throw new ArgumentException("A range names where it starts or where it ends.");
            }

            return empty ? throw new ArgumentException("A range ends before it starts.") : this;
        }
    }
}
