import type { ReactNode } from 'react';
import { getErrorMessage } from '../../ui';
import type { Loaded } from '../../hooks/useLoaded';
import { PreviewMessage } from './PreviewMessage';
import { PreviewSpinner } from './PreviewSpinner';

/** What a viewer loaded, once it has: a spinner until then, and why when it could not be. */
export function LoadedContent<T>({ loaded, children }: { loaded: Loaded<T>; children: (value: T) => ReactNode }) {
  if (loaded.status === 'loading') return <PreviewSpinner />;
  if (loaded.status === 'failed') return <PreviewMessage message={getErrorMessage(loaded.error, 'Could not read the file.')} />;
  return children(loaded.value);
}
