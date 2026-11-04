export interface SseBufferSplitResult {
  completeBlocks: string[];
  remainder: string;
}

// SSE messages are separated by a blank line. A "complete block" is everything up to (but not
// including) one of those separators. The trailing piece may be a partial message we have to
// keep until the next chunk arrives.
export function splitSseBuffer(buffer: string): SseBufferSplitResult {
  const segments = buffer.split(/\r?\n\r?\n/);
  const remainder = segments.pop() ?? '';
  return {
    completeBlocks: segments,
    remainder,
  };
}

// Extracts the joined `data:` payload from one SSE block. Lines beginning with `:` are SSE
// comments (heartbeats) and are filtered out by ignoring everything that isn't a data line —
// returns null when there is no data line, which is the right signal for "skip this block".
export function getSseData(block: string): string | null {
  const dataLines = block
    .split(/\r?\n/)
    .filter((line) => line.startsWith('data:'))
    .map((line) => line.slice(5).trimStart());

  if (dataLines.length === 0) {
    return null;
  }

  return dataLines.join('\n');
}
