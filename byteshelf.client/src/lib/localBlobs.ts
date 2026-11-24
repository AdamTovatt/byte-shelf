// Contents read into the page, shown or saved from a blob in the browser: an encrypted file never goes
// through a link the server answers, since the server only holds its encrypted bytes.

import { openTabWithoutPasswords } from './encryption/passwords';
import { UNTYPED, mediaTypeOf } from './mediaTypes';

/** How long a blob's URL is kept: enough for a tab to load it, or a save to start. */
const KEEP_MS = 5 * 60 * 1000;

/**
 * The types a blob is shown as in a tab. A blob's URL belongs to this app's origin, so anything a
 * browser could run (HTML, SVG, XML) would run as the app: those, and every other type, are saved.
 */
const SHOWN_TYPES = [/^image\/(png|jpeg|gif|webp|avif|bmp)$/, /^application\/pdf$/, /^text\/plain$/, /^audio\//, /^video\//];

/**
 * The types an element shows only when it is told them, rather than by the bytes: those of SHOWN_TYPES
 * aside, a browser could run some of these as the app.
 */
const SHOWN_BY_TYPE = [/^image\//, /^text\//, /[/+]xml$/, /[/+]json$/];

/** Whether a blob of `type` is shown in a tab rather than saved. */
export function showsInTab(type: string): boolean {
  return SHOWN_TYPES.some((pattern) => pattern.test(mediaTypeOf(type)));
}

/** A URL of a blob for an element of this page, until it is released. */
export interface ElementUrl {
  url: string;
  release: () => void;
}

/**
 * A URL an element of this page (an image, a player, a frame) loads `blob` from. The browser's own menu
 * can open what an element shows in a tab, so a blob of a type a tab must not show there gets a data:
 * URL, which never runs as the app, when its type is needed to show it; any other is loaded as untyped
 * bytes, which a tab only saves (a data: URL of something large, such as a model, fails).
 */
export async function elementUrl(blob: Blob): Promise<ElementUrl> {
  const type = mediaTypeOf(blob.type);
  const shown = showsInTab(type);
  if (!shown && SHOWN_BY_TYPE.some((pattern) => pattern.test(type))) return { url: await dataUrl(blob), release: () => {} };
  const url = URL.createObjectURL(shown ? blob : blob.slice(0, blob.size, UNTYPED));
  return { url, release: () => URL.revokeObjectURL(url) };
}

function dataUrl(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(reader.error ?? new Error('Could not read the contents.'));
    reader.readAsDataURL(blob);
  });
}

/**
 * Shows `blob` in a new tab, or saves it as `name` when it is not a type a tab shows. Answers false
 * when the browser blocked the tab, as it does once too long has passed since the user's click (a
 * password typed or a large file decrypted): open it again from a fresh click then.
 */
export function showBlob(blob: Blob, name: string): boolean {
  if (!showsInTab(blob.type)) {
    saveBlob(blob, name);
    return true;
  }
  const tab = openTabWithoutPasswords();
  if (tab) tab.location.href = keptUrl(blob);
  return tab !== null;
}

/** Saves `blob` as a file called `name`. */
export function saveBlob(blob: Blob, name: string): void {
  saveFrom(keptUrl(blob), name);
}

/**
 * Saves what `href` answers as a file called `name`, or as the server names it when `name` is empty.
 * A download rather than a navigation, so one starting does not cancel the one before it.
 */
export function saveFrom(href: string, name: string): void {
  const anchor = document.createElement('a');
  anchor.href = href;
  anchor.download = name;
  anchor.click();
}

function keptUrl(blob: Blob): string {
  const url = URL.createObjectURL(blob);
  setTimeout(() => URL.revokeObjectURL(url), KEEP_MS);
  return url;
}
