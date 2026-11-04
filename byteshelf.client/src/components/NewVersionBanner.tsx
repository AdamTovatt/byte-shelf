import { useNewVersionAvailable } from '../hooks/useNewVersionAvailable';
import { Button } from '../ui';

/** A line across the top when the server runs a newer build than this page, with a way to reload. */
export function NewVersionBanner() {
  const isStale = useNewVersionAvailable();
  if (!isStale) return null;

  return (
    <div role="status" className="flex items-center justify-center gap-3 border-b border-info-a-5 bg-info-a-3 px-4 py-2 text-body-md text-fg">
      <span>A new version of ByteShelf is available.</span>
      <Button variant="main" size="sm" onClick={() => window.location.reload()}>
        Refresh
      </Button>
    </div>
  );
}
