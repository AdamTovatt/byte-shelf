import { useNewVersionAvailable } from '../hooks/useNewVersionAvailable';
import { Banner, Button } from './ui';

export function NewVersionBanner() {
  const isStale = useNewVersionAvailable();
  if (!isStale) return null;

  return (
    <Banner
      variant="info"
      action={
        <Button variant="primary" size="sm" onClick={() => window.location.reload()}>
          Refresh
        </Button>
      }
    >
      A new version of ByteShelf is available.
    </Banner>
  );
}
