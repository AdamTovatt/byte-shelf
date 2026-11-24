/** What a plugin adds to the web UI, as `definePlugin` takes it. */
export type PluginDefinition = Readonly<Record<string, unknown>>;

const defined = new WeakSet<object>();

/** The default export of a plugin's module. */
export function definePlugin(definition: PluginDefinition): PluginDefinition {
  defined.add(definition);
  return definition;
}

/** Whether `value` was made with `definePlugin`, which plugins share with the app. */
export function isPluginDefinition(value: unknown): value is PluginDefinition {
  return typeof value === 'object' && value !== null && defined.has(value);
}
