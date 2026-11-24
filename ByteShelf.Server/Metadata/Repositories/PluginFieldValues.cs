using ByteShelfCommon;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Metadata.Repositories
{
    /// <summary>The conditions a query of files puts on <c>plugin_field_value</c>.</summary>
    internal static class PluginFieldValues
    {
        /// <summary>The condition that <paramref name="value"/>, a row of the table, is of the query's <c>file</c>: of it, or of its contents unless it is encrypted.</summary>
        public static string OfFile(string value)
        {
            return $"({value}.file_id = file.id OR ({value}.sha256 = file.sha256 AND file.encrypted = 0))";
        }

        /// <summary>
        /// The condition that the query's <c>file</c> meets <paramref name="filter"/> (normalized), adding the
        /// parameters it reads, whose names start with <paramref name="prefix"/>.
        /// </summary>
        public static string Meets(PluginFieldFilter filter, string prefix, DynamicParameters parameters)
        {
            parameters.Add(prefix + "Plugin", filter.Plugin);
            parameters.Add(prefix + "Field", filter.Field);
            return filter.Condition switch
            {
                PluginFieldCondition.Has has => has.Present ? Exists("1") : "NOT " + Exists("1"),
                PluginFieldCondition.Contains contains => Exists($"instr(value.text_fold, {Parameter("Text", contains.Text)}) > 0"),
                PluginFieldCondition.Between between => Exists(
                    $"value.number IS NOT NULL{AtLeast("number", between.Min)}{(between.Max == null ? string.Empty : $" AND value.number <= {Parameter("Max", between.Max)}")}"),
                PluginFieldCondition.During during => Exists(
                    $"value.instant IS NOT NULL{AtLeast("instant", during.From)}{(during.Before == null ? string.Empty : $" AND value.instant < {Parameter("Before", during.Before)}")}"),
                PluginFieldCondition.Is flag => Exists($"value.flag = {Parameter("Flag", flag.Flag)}"),
                _ => throw new ArgumentException($"No query asks for a {filter.Condition.GetType().Name}.", nameof(filter)),
            };

            string Exists(string condition)
            {
                return $@"EXISTS (
                    SELECT 1 FROM plugin_field_value AS value
                    WHERE value.plugin_id = @{prefix}Plugin AND value.field = @{prefix}Field AND {OfFile("value")} AND {condition})";
            }

            string AtLeast(string column, object? start)
            {
                return start == null ? string.Empty : $" AND value.{column} >= {Parameter("Min", start)}";
            }

            string Parameter(string name, object value)
            {
                parameters.Add(prefix + name, value);
                return $"@{prefix}{name}";
            }
        }
    }
}
