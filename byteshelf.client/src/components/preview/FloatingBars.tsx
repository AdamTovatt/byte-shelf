import type { ReactNode, Ref } from 'react';
import { cn } from '../../ui';
import { useMeasuredSize } from '../../ui/components/ui/useMeasuredSize';
import { useFadingControls, type FadingControls } from '../../hooks/useFadingControls';
import { barHeights } from './previewBars';

interface FloatingBarsProps {
  title: string;
  /** The controls at the end of the top bar. */
  actions: ReactNode;
  /** Where the file stands among those beside it, and the steps to them: beside the actions, or on a phone in a bar at the bottom. */
  steps: ReactNode;
  /** What the bars float over, which fills all of the room. */
  children: ReactNode;
}

/**
 * Bars of controls floating over what they control, in pills that blur what is under them, that fade when
 * nothing happens (`useFadingControls`). Their heights are given to what is drawn between them (`CLEAR_OF_BARS`).
 */
export function FloatingBars({ title, actions, steps, children }: FloatingBarsProps) {
  const controls = useFadingControls();
  const { ref: topBar, height: topBarHeight } = useMeasuredSize<HTMLDivElement>();
  const { ref: bottomBar, height: bottomBarHeight } = useMeasuredSize<HTMLDivElement>();

  return (
    <div className="relative flex min-h-0 flex-1 flex-col" style={barHeights(topBarHeight, bottomBarHeight)} {...controls.area}>
      {children}
      <Bar ref={topBar} controls={controls} className="top-0 items-start justify-between">
        <Pill controls={controls}>
          <h2 className="truncate px-3 text-body-sm font-medium text-fg">{title}</h2>
        </Pill>
        <Pill controls={controls} className="shrink-0">
          {steps && <div className="hidden sm:block">{steps}</div>}
          {actions}
        </Pill>
      </Bar>
      {steps && (
        <Bar ref={bottomBar} controls={controls} className="bottom-0 justify-center sm:hidden">
          <Pill controls={controls}>{steps}</Pill>
        </Bar>
      )}
    </div>
  );
}

/** A row of pills along an edge, which takes no clicks of its own: those go to what is under it. */
function Bar({ ref, controls, className, children }: { ref: Ref<HTMLDivElement>; controls: FadingControls; className: string; children: ReactNode }) {
  return (
    <div
      ref={ref}
      data-shown={controls.isShown}
      className={cn('pointer-events-none absolute inset-x-0 z-10 flex gap-2 p-2 transition-opacity duration-300', controls.isShown ? 'opacity-100' : 'opacity-0', className)}
    >
      {children}
    </div>
  );
}

/** Controls together, over a blur of what is under them; while hidden they take no clicks. */
function Pill({ controls, className, children }: { controls: FadingControls; className?: string; children: ReactNode }) {
  return (
    <div className={cn('flex min-w-0 items-center gap-1 rounded-full p-1 backdrop-blur-xl', controls.isShown && 'pointer-events-auto', className)} {...controls.group}>
      {children}
    </div>
  );
}
