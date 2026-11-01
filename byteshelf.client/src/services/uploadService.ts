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
  /** Told how many bytes the server has after each request. */
  onProgress?: (sentBytes: number) => void;
  signal?: AbortSignal;
}

// A file's bytes go through authorizedFetch, since openapi-fetch sends JSON bodies; the upload's
// other steps are plain API calls.

/** Stores a file; a failed chunk aborts the upload, so its reservation against the quota is released at once. */
export async function uploadFile(request: UploadRequest): Promise<ShelfFile> {
  const { shelf, path, file, overwrite, onProgress, signal } = request;
  const contentType = file.type || 'application/octet-stream';

  if (file.size <= CHUNK_BYTES) {
    const query = new URLSearchParams({ path, overwrite: String(overwrite) });
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
    body: { path, sizeBytes: file.size, contentType, overwrite },
    signal,
  }));
  const uploadPath = { path: { shelf, uploadId: session.id } };

  try {
    for (let offset = 0; offset < file.size; offset += CHUNK_BYTES) {
      const chunk = file.slice(offset, Math.min(offset + CHUNK_BYTES, file.size));
      const url = apiUrl('/api/shelves/{shelf}/uploads/{uploadId}', { shelf, uploadId: session.id }, new URLSearchParams({ offset: String(offset) }));
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
