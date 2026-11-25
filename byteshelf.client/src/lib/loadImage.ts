/**
 * Loads `url` in `image` and decodes it, so that it can be drawn whole at once. Fails when it cannot be loaded;
 * aborting `signal` ends the load, or the decode.
 */
export async function loadImage(image: HTMLImageElement, url: string, signal: AbortSignal): Promise<void> {
  signal.throwIfAborted();
  const aborted = new Promise<never>((_, reject) => {
    signal.addEventListener('abort', () => {
      // Taking an image out of the page alone lets it load on.
      image.removeAttribute('src');
      reject(signal.reason as Error);
    }, { once: true });
  });
  // Settled by whichever comes first; the rest is not waited for.
  aborted.catch(() => undefined);

  const loaded = new Promise<void>((resolve, reject) => {
    image.onload = () => resolve();
    image.onerror = () => reject(new Error('The image could not be loaded.'));
    image.src = url;
  });
  await Promise.race([loaded, aborted]);

  // A browser may refuse to decode ahead what it draws all the same, such as a very large image.
  await Promise.race([image.decode().catch(() => undefined), aborted]);
}
