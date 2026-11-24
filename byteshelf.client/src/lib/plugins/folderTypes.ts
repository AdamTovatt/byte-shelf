import type { FolderType } from './folderType';
import type { RunningPlugin } from './pluginHost';

/** What a folder type's suffix may be. */
export const FOLDER_SUFFIX = /^[a-z0-9]{2,}$/;

/** The suffix in a folder's name, in lower case, which a folder type may have; null for a name with none, or that is only one. */
export function folderSuffixOf(name: string): string | null {
  const dot = name.lastIndexOf('.');
  // Letters a to z alone: others become one of them in lower case.
  const suffix = name.slice(dot + 1).replace(/[A-Z]/g, (letter) => letter.toLowerCase());
  return dot > 0 && FOLDER_SUFFIX.test(suffix) ? suffix : null;
}

/** The type of a folder called `name`, the first plugin's by id that has its suffix; null for a plain folder. */
export function folderTypeOf(plugins: readonly RunningPlugin[], name: string): FolderType | null {
  const suffix = folderSuffixOf(name);
  return plugins.flatMap(({ folderTypes }) => folderTypes).find((type) => type.suffix === suffix) ?? null;
}
