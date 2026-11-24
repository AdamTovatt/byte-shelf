/** The type of a file stored without one (ByteShelfCommon's `ContentTypes.Default`). */
export const UNTYPED = 'application/octet-stream';

/** The charset a stored type names, as `iso-8859-1` in `text/plain; charset=iso-8859-1`; undefined when it names none. */
export function charsetOf(contentType: string): string | undefined {
  return contentType.match(/;\s*charset\s*=\s*"?([^\s";]+)/i)?.[1];
}

/** A stored type as it is compared: lowercase, without parameters such as `; charset=utf-8`. */
export function mediaTypeOf(contentType: string): string {
  return contentType.split(';')[0].trim().toLowerCase();
}
