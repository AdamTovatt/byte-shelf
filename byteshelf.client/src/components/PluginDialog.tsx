import { Suspense, type ReactNode } from 'react';
import { Spinner } from '../ui';
import { ErrorBoundary } from './ErrorBoundary';
import { FormDialog } from './FormDialog';

export interface PluginDialogProps {
  /** What it is of, which it is called while it loads and when it fails. */
  title: string;
  render: (close: () => void) => ReactNode;
  close: () => void;
}

/** A dialog a plugin draws; one that still loads, or that fails, says so instead. */
export function PluginDialog({ title, render, close }: PluginDialogProps) {
  return (
    <ErrorBoundary fallback={<FormDialog open title={title} onClose={close} error="The dialog failed." />}>
      <Suspense fallback={<FormDialog open title={title} onClose={close}><Spinner /></FormDialog>}>
        <Drawn render={render} close={close} />
      </Suspense>
    </ErrorBoundary>
  );
}

/** Calls `render` as it is drawn itself, so what that throws is caught around it. */
function Drawn({ render, close }: Pick<PluginDialogProps, 'render' | 'close'>) {
  return render(close);
}
