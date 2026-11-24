import { useMutation, useQuery, useQueryClient, type UseMutationResult, type UseQueryResult } from '@tanstack/react-query';
import { usePluginId } from '../contexts/PluginIdentity';
import { queryKeys } from '../lib/queryKeys';
import { fetchFileMetadata, setPluginDocument, type FileMetadata, type PluginFileDetails, type ShelfFile } from '../services/shelfService';
import { shelfName, useInvalidateShelf } from './useShelfQueries';

/** What a plugin keeps about a file. */
export interface PluginDocuments {
  /** What its reader found in the contents; null when nothing, or the file is encrypted. */
  contents: Record<string, unknown> | null;
  /** The document about the file, which the plugin writes; null when there is none. */
  file: Record<string, unknown> | null;
  /** The version of the document about the file, kept when it is removed; null when none was ever written. */
  fileVersion: number | null;
}

/** A write of the document about a file. */
export interface PluginDocumentWrite {
  /**
   * The whole of it, 64 KiB of JSON at most, nested at most 32 deep; null removes it. It must hold nothing
   * read from the file's plaintext: it is kept when the file is encrypted, and would outlive it.
   */
  document: Record<string, unknown> | null;
  /** The `fileVersion` it was made from: when another is there by now, the write fails with an `ApiError` of status 409. */
  expectedVersion: number | null;
}

/**
 * What the plugin whose viewer or dialog calls this keeps about a live file. Read again when a document about
 * a file changes on the file's shelf, whoever changes it; what its reader finds later is there the next time
 * it is asked for.
 */
export function usePluginDocuments(file: ShelfFile): UseQueryResult<PluginDocuments> {
  const pluginId = usePluginId();
  const queryClient = useQueryClient();
  return useQuery({
    queryKey: queryKeys.shelfContents.pluginDocuments(file.shelfId, file.id),
    queryFn: async () => fetchFileMetadata(await shelfName(queryClient, file.shelfId), file.id),
    // What is read from contents is not in the live updates.
    staleTime: 0,
    select: ({ plugins }) => documentsOf(file, plugins?.[pluginId]),
  });
}

/** Writes the document about a live file of the plugin whose viewer or dialog calls this; whoever may write the file may. */
export function useSetPluginDocument(file: ShelfFile): UseMutationResult<PluginDocuments, Error, PluginDocumentWrite> {
  const pluginId = usePluginId();
  const queryClient = useQueryClient();
  const invalidate = useInvalidateShelf();
  return useMutation({
    mutationFn: async ({ document, expectedVersion }: PluginDocumentWrite) => {
      const written = await setPluginDocument(await shelfName(queryClient, file.shelfId), file.id, pluginId, document, expectedVersion);

      // At once, so the next write is made from this one's version.
      queryClient.setQueryData(queryKeys.shelfContents.pluginDocuments(file.shelfId, file.id), (read: FileMetadata | undefined) => (
        read && { ...read, plugins: { ...read.plugins, [pluginId]: written } }
      ));
      return documentsOf(file, written);
    },
    // Refused too: what is there by now is what to write from.
    onSettled: invalidate,
  });
}

function documentsOf(file: ShelfFile, metadata: PluginFileDetails | undefined): PluginDocuments {
  return {
    // Not what was read before it was encrypted, which may still be cached.
    contents: file.encrypted ? null : metadata?.contentsDocument ?? null,
    file: metadata?.fileDocument ?? null,
    fileVersion: metadata?.fileVersion ?? null,
  };
}
