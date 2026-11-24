namespace ByteShelfCommon
{
    /// <summary>The plugins a server, or the in-memory client, has: what a filter on their fields is checked against.</summary>
    public sealed class InstalledPlugins
    {
        private readonly Dictionary<string, PluginDeclaration> _byId;

        /// <exception cref="ArgumentException">Two have one id.</exception>
        public InstalledPlugins(IEnumerable<PluginDeclaration> plugins)
        {
            All = plugins.ToList();
            _byId = All.ToDictionary(plugin => plugin.Id, StringComparer.Ordinal);
        }

        /// <summary>Every one, in the order given.</summary>
        public IReadOnlyList<PluginDeclaration> All { get; }

        /// <summary>The plugin with the id <paramref name="id"/>; null when it is not installed.</summary>
        public PluginDeclaration? Find(string id)
        {
            return _byId.GetValueOrDefault(id);
        }

        /// <summary>Each of <paramref name="filters"/> with the plugin and the field it asks about.</summary>
        /// <exception cref="ArgumentException">A plugin is not installed or declares no such field, or its kind is not one the condition can be asked of; the message names what there is.</exception>
        public IReadOnlyList<(PluginFieldFilter Filter, PluginDeclaration Plugin, PluginField Field)> Check(IEnumerable<PluginFieldFilter> filters)
        {
            ArgumentNullException.ThrowIfNull(filters);
            return filters.Select(Check).ToList();
        }

        private (PluginFieldFilter Filter, PluginDeclaration Plugin, PluginField Field) Check(PluginFieldFilter filter)
        {
            PluginDeclaration plugin = Find(filter.Plugin)
                ?? throw new ArgumentException($"There is no plugin '{filter.Plugin}' here; the plugins here are: {Names(All.Select(each => each.Id))}.");
            PluginField field = plugin.Fields.FirstOrDefault(each => each.Name == filter.Field)
                ?? throw new ArgumentException($"The plugin '{plugin.Id}' declares no field '{filter.Field}'; it declares: {Names(plugin.Fields.Select(each => each.Name))}.");

            if (filter.Condition.Kind is PluginFieldKind asked && asked != field.Kind)
            {
                throw new ArgumentException($"{plugin.Id}.{field.Name} is a {Name(field.Kind)}, which cannot be compared as a {Name(asked)}.");
            }

            return (filter, plugin, field);
        }

        private static string Names(IEnumerable<string> names)
        {
            string joined = string.Join(", ", names);
            return joined.Length == 0 ? "none" : joined;
        }

        private static string Name(PluginFieldKind kind)
        {
            return kind switch
            {
                PluginFieldKind.Text => "text",
                PluginFieldKind.Number => "number",
                PluginFieldKind.Date => "date",
                PluginFieldKind.Flag => "flag",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
        }
    }
}
