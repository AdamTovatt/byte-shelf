import { createContext, useContext, type ReactNode } from 'react';

const PluginIdContext = createContext<string | null>(null);

/** Draws what the plugin `id` adds, or for null what is the app's own. */
export function PluginIdentity({ id, children }: { id: string | null; children: ReactNode }) {
  return <PluginIdContext.Provider value={id}>{children}</PluginIdContext.Provider>;
}

/** The id of the plugin whose viewer or dialog is being drawn; fails anywhere else. */
export function usePluginId(): string {
  const id = useContext(PluginIdContext);
  if (id === null) throw new Error('This is for what a plugin draws: one of its viewers or dialogs.');
  return id;
}
