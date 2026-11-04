import type { ReactNode, ButtonHTMLAttributes } from 'react';
import { BusyOutline } from './BusyOutline';
import { useBusyOutlineLifecycle } from './useBusyOutlineLifecycle';
import { Spinner } from './Spinner';
import { Squircle } from './Squircle';
import { buttonRendering, type ButtonVariant, type ButtonSize, type ButtonShape } from './buttonStyles';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /**
   * Silhouette — the size token's corner by default, a fully-round `pill`, or the tighter `chip`
   * corner for a control inside a dense row. See {@link ButtonShape}: on a filled variant the corner
   * cannot be reached from `className` at all, which is why it is a prop.
   */
  shape?: ButtonShape;
  icon?: ReactNode;
  loading?: boolean;
  /**
   * Work is in flight: a stroke travels around the button's edge while its label, size and icon stay
   * exactly as they were.
   *
   * **Distinct from {@link loading}, not a softer spelling of it.** `loading` replaces the whole
   * content with a spinner and the word "Loading...", which changes the button's width — fine for a
   * button standing alone, and disruptive for one in a row or a bar, where every control after it
   * moves. It also disables the button; this does not. Reach for `busy` when the button has to keep
   * saying what it is, and pass `disabled` yourself if a second press would be wrong.
   *
   * Use it for work whose outcome arrives somewhere else — a download that raises a toast, a save
   * that updates the thing beside it. If the button's own content is what changes when the work
   * lands, `loading` is the honest one.
   *
   * Ignored while `loading` is set, which is the only combination of the two that means anything —
   * they are two renderings of one state, and both at once is it said twice. `disabled busy` is left
   * to the caller and is usually wrong: the variants fade a disabled button to 40% opacity, taking
   * the outline with it, so the control ends up greyed out AND animating.
   */
  busy?: boolean;
  children?: ReactNode;
}

export function Button({
  variant = 'secondary',
  size = 'md',
  shape = 'default',
  icon,
  loading = false,
  busy = false,
  disabled,
  className = '',
  children,
  ...props
}: ButtonProps) {
  // Filled variants render with squircle corners; the Squircle layer paints fill/border/shadow behind
  // the content. Transparent variants and pills (squircle null) keep the plain rounded button.
  const { className: boxClassName, squircle, busyOutline, busyPositionClassName } =
    buttonRendering(variant, size, shape);
  // `loading` wins: it already replaces the content with a spinner and the word "Loading...", and a
  // travelling outline around that is the same state said twice in two visual languages. Not an
  // assertion, because a caller can reach this innocently — a shared button forwarding both from
  // props it does not control — and refusing to render is a better answer than a crash in production.
  const outlineRequested = busy && !loading;
  // Outlives `busy` by the length of its exit — the stroke shrinks back into itself rather than
  // being cut off, which is only possible if the layer is still mounted to do it.
  const outline = useBusyOutlineLifecycle(outlineRequested);
  return (
    <button
      // Positioning only while the outline is on screen, and only where the box does not already
      // carry it — `busyPositionClassName` is empty on the squircle path, which has `relative isolate`
      // of its own. Keyed on the layer's own lifetime rather than on `busy`, or the box would lose its
      // positioning while the exit was still playing.
      className={`${boxClassName} ${outline.render ? busyPositionClassName : ''} ${className}`}
      disabled={disabled || loading}
      // Keyed on `busy || loading` rather than on the outline's lifetime: the button stops being busy
      // when the work does, whatever the stroke is still doing on its way out.
      //
      // What this is and is not worth: it is the platform's own way to say a control is working, and
      // it costs an attribute — but most screen readers do not announce it changing, so it is a fact
      // available on inspection rather than a notification. A caller whose work matters to a
      // non-sighted user owns a live region for it; `EditorHeaderMenu` is the worked example, and it
      // puts that region OUTSIDE the button on purpose, since text inside one joins its accessible
      // name and would rename the control mid-press.
      aria-busy={busy || loading || undefined}
      {...props}
    >
      {squircle && <Squircle {...squircle} />}
      {/* In front of the surface rather than behind it, so a filled variant does not paint over its
          own outline. Which silhouette it traces is `buttonRendering`'s answer, spread the same way
          the Squircle's props are on the line above — the two layers describe the same corner and are
          resolved in the same place. */}
      {outline.render && (
        <BusyOutline
          {...busyOutline}
          exiting={outline.exiting}
          onAnimationEnd={outline.onAnimationEnd}
        />
      )}
      {loading ? (
        <>
          {/* The button's own ink: the brand arc all but vanished on `main`'s near-black fill. */}
          <Spinner size="sm" tone="inherit" />
          Loading...
        </>
      ) : (
        <>
          {icon && <span className="flex items-center justify-center">{icon}</span>}
          {children}
        </>
      )}
    </button>
  );
}
