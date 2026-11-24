import type { Viewer } from '../preview/viewerRegistry';
import type { FileAction } from './fileAction';
import type { FolderType } from './folderType';
import { FOLDER_SUFFIX } from './folderTypes';

/** What a plugin adds to the web UI, as `definePlugin` takes it. */
export interface PluginDefinition {
  /** What the preview shows files with; one that loads code when it is first shown is a `lazy` component. */
  readonly viewers?: readonly Viewer[];
  readonly fileActions?: readonly FileAction[];
  readonly folderTypes?: readonly FolderType[];
}

/** What a plugin adds to the web UI, each list there. */
export type PluginParts = Required<PluginDefinition>;

const defined = new WeakSet<object>();

/** The default export of a plugin's module. */
export function definePlugin(definition: PluginDefinition): PluginDefinition {
  defined.add(definition);
  return definition;
}

/**
 * What the plugin whose module exports `exported` adds. Fails on what the app would otherwise fail on as it
 * draws, where that would take the page down: a plugin's module may be built without its types checked.
 */
export function partsOf(exported: unknown): PluginParts {
  if (typeof exported !== 'object' || exported === null || !defined.has(exported)) throw new Error('Its module has no default export made with definePlugin.');

  const { viewers = [], fileActions = [], folderTypes = [] } = exported as Record<keyof PluginDefinition, unknown>;
  return {
    viewers: listOf<Viewer, 'id'>('viewers', viewers, 'id', ({ mediaTypes, extensions, component, unavailable }) => (
      isTexts(mediaTypes) && isTexts(extensions) && isComponent(component) && (unavailable === undefined || typeof unavailable === 'function')
    )),
    fileActions: listOf<FileAction, 'id'>('fileActions', fileActions, 'id', ({ label, showsFor, run }) => (
      typeof label === 'string' && typeof showsFor === 'function' && typeof run === 'function'
    )),
    folderTypes: listOf<FolderType, 'suffix'>('folderTypes', folderTypes, 'suffix', ({ suffix, label, icon, component }) => (
      FOLDER_SUFFIX.test(suffix) && typeof label === 'string' && isComponent(icon) && isComponent(component)
    )),
  };
}

type Unchecked<T> = { [Key in keyof T]?: unknown };

/** `list`, named `name`, of what has a `key` apart from the others' and which `isOne` takes. */
function listOf<T extends Record<Key, string>, Key extends string>(
  name: string, list: unknown, key: Key, isOne: (each: Unchecked<T> & Record<Key, string>) => boolean,
): readonly T[] {
  if (!Array.isArray(list)) throw new Error(`Its ${name} are not a list.`);

  const keys = new Set<unknown>();
  for (const each of list as ((Unchecked<T> & Record<Key, string>) | null | undefined)[]) {
    if (!each || typeof each[key] !== 'string' || !isOne(each)) throw new Error(`One of its ${name} lacks what each must have.`);
    if (keys.has(each[key])) throw new Error(`Two of its ${name} have the ${key} ${each[key]}.`);
    keys.add(each[key]);
  }
  return list as T[];
}

function isTexts(value: unknown): boolean {
  return Array.isArray(value) && value.every((each) => typeof each === 'string');
}

/** A function, or an object as `lazy` and `memo` make. */
function isComponent(value: unknown): boolean {
  return typeof value === 'function' || (typeof value === 'object' && value !== null);
}
