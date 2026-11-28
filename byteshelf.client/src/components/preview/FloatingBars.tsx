import { createContext, useCallback, useContext, useLayoutEffect, useMemo, useState, type ReactNode, type Ref } from 'react';
import { createPortal } from 'react-dom';
import { IconButton, cn, type IconButtonProps } from '../../ui';
import { DRAGS_ITSELF } from '../../lib/dragsItself';
import { useMeasuredSize } from '../../ui/components/ui/useMeasuredSize';
import { useMediaQuery } from '../../ui/hooks/useMediaQuery';
import { useFadingControls, type FadingControls } from '../../hooks/useFadingControls';
import { SM } from '../../lib/screens';
import { barHeights } from './previewBars';

/** Wider than a phone, where the `sm:` classes that lay out the bars apply. */
const NOT_A_PHONE = `(min-width: ${SM})`;

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
 * nothing happens (`useFadingControls`), with the viewer's own controls at the bottom (`PreviewControls`). Their
 * heights are given to what is drawn between them (`CLEAR_OF_BARS`).
 * On a phone they stand further from the edges, and a bar's pills are as tall as its tallest.
 */
export function FloatingBars({ title, actions, steps, children }: FloatingBarsProps) {
  const controls = useFadingControls();
  const { ref: topBar, height: topBarHeight } = useMeasuredSize<HTMLDivElement>();
  const { ref: bottomBar, height: bottomBarHeight } = useMeasuredSize<HTMLDivElement>();
  const [slot, setSlot] = useState<HTMLDivElement | null>(null);
  const [viewerControls, setViewerControls] = useState(0);
  const addControls = useCallback(() => {
    setViewerControls((count) => count + 1);
    return () => setViewerControls((count) => count - 1);
  }, []);
  const controlsSlot = useMemo(() => ({ slot, addControls }), [slot, addControls]);
  const hasViewerControls = viewerControls > 0;

  return (
    <div className="relative flex min-h-0 flex-1 flex-col" style={barHeights(topBarHeight, bottomBarHeight)} {...controls.area}>
      <ControlsSlot.Provider value={controlsSlot}>{children}</ControlsSlot.Provider>
      <Bar ref={topBar} controls={controls} className="top-0 items-stretch justify-between pt-4 sm:items-start sm:pt-2">
        <Pill controls={controls}>
          <h2 className="truncate px-3 text-body-sm font-medium text-fg">{title}</h2>
        </Pill>
        <Pill controls={controls} className="shrink-0">
          {steps && <div className="hidden sm:block">{steps}</div>}
          {actions}
        </Pill>
      </Bar>
      <Bar
        ref={bottomBar}
        controls={controls}
        className={cn('bottom-0 flex-col items-center pb-4 sm:pb-2', !hasViewerControls && (steps ? 'sm:hidden' : 'hidden'))}
      >
        <Pill controls={controls} className={cn('w-full sm:max-w-3xl', !hasViewerControls && 'hidden')}>
          <div ref={setSlot} {...(hasViewerControls && { [DRAGS_ITSELF]: '' })} className="flex min-w-0 flex-1 items-center gap-1" />
        </Pill>
        {steps && (
          <div className="sm:hidden">
            <Pill controls={controls}>{steps}</Pill>
          </div>
        )}
      </Bar>
    </div>
  );
}

/**
 * Where the viewer's controls go: the slot of the bottom bar, shown while some are drawn in it. `addControls` counts
 * one drawing them, and answers what counts them gone.
 */
const ControlsSlot = createContext<{ slot: HTMLElement | null; addControls: () => () => void } | null>(null);

/**
 * A viewer's controls of its own, as a player's, in a pill of the preview's bottom bar, the width of the bar up to
 * a limit, that fades with its bars; on a phone it is above the steps. A drag on them is theirs, not a swipe of the
 * preview's. Drawn nowhere outside a preview.
 */
export function PreviewControls({ children }: { children: ReactNode }) {
  const controlsSlot = useContext(ControlsSlot);
  const addControls = controlsSlot?.addControls;
  useLayoutEffect(() => addControls?.(), [addControls]);
  return controlsSlot?.slot ? createPortal(children, controlsSlot.slot) : null;
}

/** A control in the bars, larger on a phone, where a finger taps it. */
export function BarButton(props: Omit<IconButtonProps, 'variant' | 'size' | 'shape'>) {
  // A size rather than classes, as the button draws its glyph at the size's number of pixels.
  const isPhone = !useMediaQuery(NOT_A_PHONE);
  return <IconButton variant="ghost" size={isPhone ? 'md' : 'sm'} shape="pill" {...props} />;
}

interface BarTextButtonProps {
  /** What it does, as read out and shown on hover. */
  label: string;
  /** Whether it is pressed, for a control that is on or off; left out for one that is neither. */
  isPressed?: boolean;
  onClick: () => void;
  /** The word or two it shows. */
  children: string;
}

/** A control in the bars that shows a word or two rather than a glyph, as a player's speed; as tall as `BarButton`. */
export function BarTextButton({ label, isPressed, onClick, children }: BarTextButtonProps) {
  const isPhone = !useMediaQuery(NOT_A_PHONE);
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      aria-pressed={isPressed}
      onClick={onClick}
      className={cn(
        'shrink-0 whitespace-nowrap rounded-full px-2 text-body-sm font-medium tabular-nums text-muted transition-all hover:bg-surface hover:text-fg aria-pressed:bg-surface aria-pressed:text-fg',
        isPhone ? 'h-9' : 'h-6',
      )}
    >
      {children}
    </button>
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
