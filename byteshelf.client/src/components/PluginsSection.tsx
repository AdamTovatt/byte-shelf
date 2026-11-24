import { CardList } from './CardList';
import { LoadError } from './LoadError';
import { SectionTitle } from './SectionTitle';
import { usePluginHost } from '../hooks/usePluginHost';
import { usePlugins } from '../hooks/usePlugins';

/** The server's plugins in an admin's settings, and what is wrong with one or why it was skipped; nothing on a server without any. */
export function PluginsSection() {
  const { data, error } = usePlugins();
  const { failures } = usePluginHost();
  const plugins = data?.plugins ?? [];
  const skipped = data?.skipped ?? [];
  if (!error && plugins.length === 0 && skipped.length === 0) return null;

  return (
    <section className="space-y-4">
      <SectionTitle>Plugins</SectionTitle>
      <LoadError error={error} failure="Could not load the plugins" />
      {data && (
        <CardList>
          {plugins.map((plugin) => (
            <PluginRow
              key={plugin.id}
              name={plugin.name}
              details={`Version ${plugin.version}`}
              problems={[failures.has(plugin.id) && `Its part in the web UI failed: ${failures.get(plugin.id)}`, plugin.readerProblem]}
            />
          ))}
          {skipped.map((plugin) => (
            <PluginRow
              key={plugin.folder}
              name={plugin.name ?? plugin.id ?? 'Unknown plugin'}
              details={[plugin.version && `Version ${plugin.version}`, plugin.folder].filter(Boolean).join(' · ')}
              problems={[`Skipped: ${plugin.reason}`]}
            />
          ))}
        </CardList>
      )}
    </section>
  );
}

function PluginRow({ name, details, problems }: { name: string; details: string; problems: (string | false | null)[] }) {
  return (
    <div className="px-4 py-3">
      <div className="truncate text-body-md text-fg">{name}</div>
      <div className="truncate text-body-sm text-muted">{details}</div>
      {problems.map((problem) => problem && <div key={problem} className="text-body-sm text-danger-11 [overflow-wrap:anywhere]">{problem}</div>)}
    </div>
  );
}
