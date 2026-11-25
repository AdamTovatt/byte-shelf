/** The size of an image in pixels, upright. */
export interface ImageSize {
  width: number;
  height: number;
}

/** What the plugin's reader recorded of an image. */
export interface RecordedImage extends ImageSize {
  /** The URL of a tiny copy, to show blurred. */
  placeholder: string;
}

/** What `document`, the reader's about a file's contents, says of an image; null when there is none or it says something else. */
export function recordedImage(document: Record<string, unknown> | null | undefined): RecordedImage | null {
  const { width, height, placeholder } = document ?? {};
  if (!isSide(width) || !isSide(height) || typeof placeholder !== 'string') return null;
  return { width, height, placeholder };
}

function isSide(value: unknown): value is number {
  return typeof value === 'number' && value > 0;
}
