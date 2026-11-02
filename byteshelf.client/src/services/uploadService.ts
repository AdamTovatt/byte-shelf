import { apiClient, apiUrl, authorizedFetch, throwIfFailed, unwrapNoContentResponse, unwrapResponse } from './apiClient';
import type { ShelfFile } from './shelfService';

/** Files up to this size go in one request, larger ones as a resumable upload in chunks of this size. */
export const CHUNK_BYTES = 8 * 1024 * 1024;

export interface UploadRequest {
  shelf: string;
  /** Where to store the file, its name included. */
  path: string;
  file: Blob;
  overwrite: boolean;
  /** The file is an age file encrypted with a password, typed as what it holds. */
  encrypted?: boolean;
  /** Told how many bytes the server has after each request. */
  onProgress?: (sentBytes: number) => void;
  signal?: AbortSignal;
}

// A file's bytes go through authorizedFetch, since openapi-fetch sends JSON bodies; the upload's
// other steps are plain API calls.

/** Stores a file; a failed chunk aborts the upload, so its reservation against the quota is released at once. */
export async function uploadFile(request: UploadRequest): Promise<ShelfFile> {
  const { shelf, path, file, overwrite, encrypted = false, onProgress, signal } = request;
  const contentType = file.type || 'application/octet-stream';

  if (file.size <= CHUNK_BYTES) {
    const query = new URLSearchParams({ path, overwrite: String(overwrite) });
    if (encrypted) query.set('encrypted', 'true');
    const response = await authorizedFetch(apiUrl('/api/shelves/{shelf}/content', { shelf }, query), {
      method: 'PUT',
      headers: { 'Content-Type': contentType },
      body: file,
      signal,
    });
    await throwIfFailed(response);
    const stored = (await response.json()) as ShelfFile;
    onProgress?.(file.size);
    return stored;
  }

  const session = await unwrapResponse(apiClient.POST('/api/shelves/{shelf}/uploads', {
    params: { path: { shelf } },
    body: { path, sizeBytes: file.size, contentType, overwrite, encrypted },
    signal,
  }));
  return sendInChunks(shelf, session.id, file, onProgress, signal);
}

export interface ContentSwap {
  shelf: string;
  /** The file as it was read: its id names it, and its SHA-256 is what its contents must still be. */
  file: ShelfFile;
  content: Blob;
  /** Whether `content` is an age file: true encrypts the file, false decrypts it. */
  encrypted: boolean;
  signal?: AbortSignal;
}

/**
 * Swaps a file's contents in place, keeping its id, path, type and description: encrypting a plain
 * file or decrypting an encrypted one. Its old contents are gone at once, never to the trash. Refused
 * (409) when it changed since it was read. Up to a chunk goes in one request, more as an upload.
 */
export async function swapContent({ shelf, file, content, encrypted, signal }: ContentSwap): Promise<ShelfFile> {
  if (content.size <= CHUNK_BYTES) {
    const query = new URLSearchParams({ id: file.id, sha256: file.sha256, encrypted: String(encrypted) });
    const response = await authorizedFetch(apiUrl('/api/shelves/{shelf}/content/swap', { shelf }, query), { method: 'PUT', body: content, signal });
    await throwIfFailed(response);
    return (await response.json()) as ShelfFile;
  }

  const session = await unwrapResponse(apiClient.POST('/api/shelves/{shelf}/uploads/swap', {
    params: { path: { shelf } },
    body: { fileId: file.id, sha256: file.sha256, sizeBytes: content.size, encrypted },
    signal,
  }));
  return sendInChunks(shelf, session.id, content, undefined, signal);
}

/** Sends `file` to the upload `uploadId` a chunk at a time and completes it; a failure aborts the upload. */
async function sendInChunks(shelf: string, uploadId: string, file: Blob, onProgress: ((sentBytes: number) => void) | undefined, signal: AbortSignal | undefined): Promise<ShelfFile> {
  const uploadPath = { path: { shelf, uploadId } };

  try {
    for (let offset = 0; offset < file.size; offset += CHUNK_BYTES) {
      const chunk = file.slice(offset, Math.min(offset + CHUNK_BYTES, file.size));
      const url = apiUrl('/api/shelves/{shelf}/uploads/{uploadId}', { shelf, uploadId }, new URLSearchParams({ offset: String(offset) }));
      await throwIfFailed(await authorizedFetch(url, { method: 'PUT', body: chunk, signal }));
      onProgress?.(offset + chunk.size);
    }

    return await unwrapResponse(apiClient.POST('/api/shelves/{shelf}/uploads/{uploadId}/complete', { params: uploadPath, signal }));
  } catch (error) {
    // Not tied to `signal`: a cancelled upload is the one most worth aborting.
    void unwrapNoContentResponse(apiClient.DELETE('/api/shelves/{shelf}/uploads/{uploadId}', { params: uploadPath })).catch(() => undefined);
    throw error;
  }
}
