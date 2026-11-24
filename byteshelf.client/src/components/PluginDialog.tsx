import { Suspense } from 'react';
import { Spinner } from '../ui';
import { PluginIdentity } from '../contexts/PluginIdentity';
import { ErrorBoundary } from './ErrorBoundary';
import { FormDialog } from './FormDialog';
import type { DrawDialog } from '../lib/drawDialog';

interface PluginDialogProps {
  pluginId: string;
  /** What it is of, which it is called while it loads and when it fails. */
  title: string;
  draw: DrawDialog;
  close: () => void;
}

/** A dialog a plugin draws; one that still loads, or that fails, says so instead. */
export function PluginDialog({ pluginId, title, draw, close }: PluginDialogProps) {
  return (
    <ErrorBoundary fallback={<FormDialog open title={title} onClose={close} error="The dialog failed." />}>
      <Suspense fallback={<FormDialog open title={title} onClose={close}><Spinner /></FormDialog>}>
        <PluginIdentity id={pluginId}>
          <Drawn draw={draw} close={close} />
        </PluginIdentity>
      </Suspense>
    </ErrorBoundary>
  );
}

/** Calls `draw` as it is drawn itself, so what that throws is caught around it. */
function Drawn({ draw, close }: Pick<PluginDialogProps, 'draw' | 'close'>) {
  return draw(close);
}
