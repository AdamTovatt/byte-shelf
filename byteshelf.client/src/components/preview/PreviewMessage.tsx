import type { ReactNode } from 'react';
import { EmptyState } from '../../ui';

/** Said in the middle of the preview instead of the file, with what can be done about it. */
export function PreviewMessage({ message, action }: { message: string; action?: ReactNode }) {
  return (
    <div className="flex min-h-0 flex-1 items-center justify-center">
      <EmptyState variant="full" message={message} action={action} />
    </div>
  );
}
