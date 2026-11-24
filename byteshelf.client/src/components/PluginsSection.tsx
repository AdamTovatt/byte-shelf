import { CardList } from './CardList';
import { LoadError } from './LoadError';
import { SectionTitle } from './SectionTitle';
import { usePlugins } from '../hooks/usePlugins';

/** The server's plugins in an admin's settings, and why one was skipped; nothing on a server without any. */
export function PluginsSection() {
  const { data, error } = usePlugins();
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
            <PluginRow key={plugin.id} name={plugin.name} details={`Version ${plugin.version}`} />
          ))}
          {skipped.map((plugin) => (
            <PluginRow
              key={plugin.folder}
              name={plugin.name ?? plugin.id ?? 'Unknown plugin'}
              details={[plugin.version && `Version ${plugin.version}`, plugin.folder].filter(Boolean).join(' · ')}
              problem={plugin.reason}
            />
          ))}
        </CardList>
      )}
    </section>
  );
}

function PluginRow({ name, details, problem }: { name: string; details: string; problem?: string }) {
  return (
    <div className="px-4 py-3">
      <div className="truncate text-body-md text-fg">{name}</div>
      <div className="truncate text-body-sm text-muted">{details}</div>
      {problem && <div className="text-body-sm text-danger-11 [overflow-wrap:anywhere]">Skipped: {problem}</div>}
    </div>
  );
}
