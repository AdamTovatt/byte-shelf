/** The type of a file stored without one (ByteShelfCommon's `ContentTypes.Default`). */
export const UNTYPED = 'application/octet-stream';

/** A stored type as it is compared: lowercase, without parameters such as `; charset=utf-8`. */
export function mediaTypeOf(contentType: string): string {
  return contentType.split(';')[0].trim().toLowerCase();
}
