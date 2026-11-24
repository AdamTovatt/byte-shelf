import type { ReactNode } from 'react';
import type { Reloadable } from '../../hooks/useLoaded';
import { PreviewFailure } from './PreviewFailure';
import { PreviewSpinner } from './PreviewSpinner';

/** What a viewer loaded, once it has: a spinner until then, and why, and trying again, when it could not be. */
export function LoadedContent<T>({ loaded, children }: { loaded: Reloadable<T>; children: (value: T) => ReactNode }) {
  if (loaded.status === 'loading') return <PreviewSpinner />;
  if (loaded.status === 'failed') return <PreviewFailure error={loaded.error} onTryAgain={loaded.reload} />;
  return children(loaded.value);
}
