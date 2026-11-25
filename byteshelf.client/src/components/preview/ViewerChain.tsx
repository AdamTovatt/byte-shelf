import { Suspense, createContext, useContext, type ReactNode } from 'react';
import { PluginIdentity } from '../../contexts/PluginIdentity';
import { contentsKey } from '../../lib/preview/fileContents';
import type { ShownBy, ViewerProps } from '../../lib/preview/viewerRegistry';
import { ErrorBoundary } from '../ErrorBoundary';
import { PreviewSpinner } from './PreviewSpinner';

interface ViewerChainProps extends Omit<ViewerProps, 'Fallback'> {
  /** The viewers that show the file, in order: the first does, and each one's `Fallback` is the next. */
  viewers: readonly ShownBy[];
  /** Shown in place of a viewer that throws while rendering. */
  failed: ReactNode;
  /** Shown by the last one's `Fallback`. */
  none: ReactNode;
}

/** What comes after the viewer it is given to: one context for every viewer, so its `Fallback` is one component that never changes. */
const AfterViewer = createContext<ViewerChainProps | null>(null);

/** A file in the first of `viewers`, in an error boundary and drawn as its plugin's. */
export function ViewerChain({ viewers: [first, ...rest], failed, none, ...props }: ViewerChainProps) {
  if (!first) return none;
  const { viewer: { id, component: Viewer }, pluginId } = first;
  return (
    <ErrorBoundary key={`${contentsKey(props.file)} ${pluginId} ${id}`} fallback={failed}>
      <Suspense fallback={<PreviewSpinner />}>
        <PluginIdentity id={pluginId}>
          <AfterViewer.Provider value={{ ...props, viewers: rest, failed, none }}>
            <Viewer {...props} Fallback={Fallback} />
          </AfterViewer.Provider>
        </PluginIdentity>
      </Suspense>
    </ErrorBoundary>
  );
}

/** The file in the viewer after the one that draws this. */
function Fallback() {
  const after = useContext(AfterViewer);
  if (!after) throw new Error('A viewer\'s Fallback is drawn only by that viewer.');
  return <ViewerChain {...after} />;
}
