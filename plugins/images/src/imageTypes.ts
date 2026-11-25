import { mediaTypeOf } from '@byteshelf/plugin';

const FAMILY = 'image';

/** The types the viewer takes, as the manifest declares them. */
export const IMAGE_TYPES: readonly string[] = [`${FAMILY}/*`];

/** Whether a file of `contentType` is of the types the viewer takes, as the app matches them. */
export function isImage(contentType: string): boolean {
  return mediaTypeOf(contentType).startsWith(`${FAMILY}/`);
}
