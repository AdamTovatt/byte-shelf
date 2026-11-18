import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../lib/queryKeys';
import { ApiError } from '../services/apiClient';
import { createDownloadLink, fetchLinks, revokeLink } from '../services/linkService';

export function useLinks(shelf: string) {
  return useQuery({ queryKey: queryKeys.links.list(shelf), queryFn: () => fetchLinks(shelf) });
}

/** Refreshes every shelf's links, after this app made or revoked one. */
function useInvalidateLinks() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.links.root });
  };
}

/** Makes a share link to a file, which the shelf's links then list. */
export function useCreateDownloadLink(shelf: string) {
  const invalidate = useInvalidateLinks();
  return useMutation({ mutationFn: (fileId: string) => createDownloadLink(shelf, fileId), onSuccess: invalidate });
}

/**
 * Revokes a link. One the server no longer has (it ran out, was revoked elsewhere, or its file was
 * purged) counts as revoked, since it already serves nothing; whatever the answer, the list is
 * refreshed so a row that no longer works does not stay.
 */
export function useRevokeLink(shelf: string) {
  const invalidate = useInvalidateLinks();
  return useMutation({
    mutationFn: async (linkId: string) => {
      try {
        await revokeLink(shelf, linkId);
      } catch (error) {
        if (!(error instanceof ApiError && error.status === 404)) throw error;
      }
    },
    onSettled: invalidate,
  });
}
