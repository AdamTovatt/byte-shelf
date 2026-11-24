using System.Globalization;

namespace ByteShelfCommon
{
    /// <summary>
    /// A criterion on a field a plugin declares. <see cref="ToString"/> writes it as a query string, the CLI
    /// and MCP take it, and <see cref="TryParse(string?, out PluginFieldFilter)"/> reads it: <c>plugin.field</c>
    /// has a value, <c>!plugin.field</c> has none, <c>plugin.field~text</c> contains the text,
    /// <c>plugin.field=1..5</c> is a number between two (either may be left out, and one alone is that number),
    /// <c>plugin.field=2026-01-01..2026-02-01</c> an instant from one and before another, each a date, which is
    /// its start in UTC, or a date and time with an offset, and <c>plugin.field=true</c> or <c>=false</c> a flag.
    /// </summary>
    public sealed record PluginFieldFilter
    {
        internal const string RangeSeparator = "..";

        private delegate bool Reader<T>(string text, out T value);

        /// <param name="plugin">The plugin's id.</param>
        /// <param name="field">The name of the field it declares.</param>
        /// <param name="condition">What is asked of it.</param>
        public PluginFieldFilter(string plugin, string field, PluginFieldCondition condition)
        {
            ArgumentNullException.ThrowIfNull(plugin);
            ArgumentNullException.ThrowIfNull(field);
            ArgumentNullException.ThrowIfNull(condition);
            Plugin = plugin;
            Field = field;
            Condition = condition;
        }

        /// <summary>The plugin's id.</summary>
        public string Plugin { get; }

        /// <summary>The name of the field it declares.</summary>
        public string Field { get; }

        /// <summary>What is asked of it.</summary>
        public PluginFieldCondition Condition { get; }

        /// <summary>The filter <paramref name="text"/> writes.</summary>
        /// <exception cref="ArgumentException">It writes none.</exception>
        public static PluginFieldFilter Parse(string text)
        {
            return TryParse(text, out PluginFieldFilter? filter)
                ? filter!
                : throw new ArgumentException(
                    $"'{text}' is not a filter on a plugin's field: plugin.field, !plugin.field, plugin.field~text, plugin.field=1..5, plugin.field=2026-01-01..2026-02-01 or plugin.field=true.",
                    nameof(text));
        }

        /// <summary>Reads the filter <paramref name="text"/> writes; false when it writes none. Binding a query string reads it here.</summary>
        public static bool TryParse(string? text, out PluginFieldFilter? filter)
        {
            filter = null;
            if (text == null)
            {
                return false;
            }

            bool absent = text.StartsWith('!');
            string rest = absent ? text[1..] : text;
            int operation = rest.AsSpan().IndexOfAny('~', '=');
            string path = operation < 0 ? rest : rest[..operation];
            int dot = path.IndexOf('.', StringComparison.Ordinal);
            if (dot <= 0 || !PluginField.IsName(path[(dot + 1)..]) || (absent && operation >= 0))
            {
                return false;
            }

            PluginFieldCondition? condition = operation < 0 ? new PluginFieldCondition.Has(!absent)
                : rest[operation] == '~' ? new PluginFieldCondition.Contains(rest[(operation + 1)..])
                : ReadValue(rest[(operation + 1)..]);

            filter = condition == null ? null : new PluginFieldFilter(path[..dot], path[(dot + 1)..], condition);
            return filter != null;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Condition.Write($"{Plugin}.{Field}");
        }

        /// <summary>The filter checked and put the way it is compared.</summary>
        /// <exception cref="ArgumentException">Its condition is not allowed.</exception>
        internal PluginFieldFilter Normalize()
        {
            try
            {
                return new PluginFieldFilter(Plugin, Field, Condition.Normalize());
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException($"{Plugin}.{Field}: {exception.Message}", exception);
            }
        }

        private static PluginFieldCondition? ReadValue(string value)
        {
            if (value is "true" or "false")
            {
                return new PluginFieldCondition.Is(value == "true");
            }

            int separator = value.IndexOf(RangeSeparator, StringComparison.Ordinal);
            string start = separator < 0 ? value : value[..separator];
            string end = separator < 0 ? value : value[(separator + RangeSeparator.Length)..];
            if (start.Length == 0 && end.Length == 0)
            {
                return null;
            }

            if (TryReadEnd(start, TryReadNumber, out double? min) && TryReadEnd(end, TryReadNumber, out double? max))
            {
                return new PluginFieldCondition.Between(min, max);
            }

            return separator >= 0 && TryReadEnd(start, TryReadTime, out DateTimeOffset? from) && TryReadEnd(end, TryReadTime, out DateTimeOffset? before)
                ? new PluginFieldCondition.During(from, before)
                : null;
        }

        /// <summary>One end of a range; a blank one is left open.</summary>
        private static bool TryReadEnd<T>(string text, Reader<T> read, out T? value)
            where T : struct
        {
            value = null;
            if (text.Length == 0)
            {
                return true;
            }

            bool isRead = read(text, out T end);
            value = isRead ? end : null;
            return isRead;
        }

        private static bool TryReadNumber(string text, out double number)
        {
            return double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out number)
                && double.IsFinite(number);
        }

        private static bool TryReadTime(string text, out DateTimeOffset time)
        {
            return DateTimeOffset.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time)
                || PluginFieldValue.TryReadInstant(text, out time);
        }
    }
}
