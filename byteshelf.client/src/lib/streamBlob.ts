/** A stream gathered into a Blob typed `type`, which the browser may keep on disk rather than in memory. */
export async function streamBlob(stream: ReadableStream<Uint8Array>, type: string): Promise<Blob> {
  return new Blob([await new Response(stream).blob()], { type });
}
