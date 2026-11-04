import { statusDotClass, type StatusTone } from '../../lib/statusTone';

/**
 * The bar's fill colour for a tone.
 *
 * Every tone but `brand` resolves through {@link statusDotClass}, so the bar cannot end up a colour
 * behind the dots, chips and badges after a hue retune — the drift `statusTone.ts` describes, and what a
 * privately-declared tone subset eventually produces. This component used to declare one of those
 * instead of taking the shared vocabulary.
 *
 * `brand` is the one override, and deliberately: a progress bar paints a wide band rather than a 6px
 * dot, and it has always used the brand *gradient* there. Resolving it to the flat step 9 would repaint
 * every upload, parse and storage bar in the product as a side effect of adding a tone.
 */
function fillClass(tone: StatusTone): string {
  return tone === 'brand' ? 'bg-brand-gradient' : statusDotClass(tone);
}

/**
 * How faint the in-flight band is against the solid one. The same hue at lower alpha rather than a
 * second colour, which is what makes it read as the same work at an earlier moment — see
 * {@link ProgressBarProps.inFlightProgress}.
 *
 * **Half, because it was measured rather than chosen.** The faded band has to be distinguishable from
 * BOTH neighbours at once — the solid band on one side and the empty track on the other — and the two
 * gaps trade off directly: every step of alpha that separates it from the track moves it toward the
 * solid. Exported so `ProgressBar.test.tsx` resolves this class against the real palette and asserts
 * both gaps in both themes, which is the only reason to trust the number rather than the eye.
 */
export const IN_FLIGHT_FADE_CLASS = 'opacity-50';

/**
 * The tones a SECOND band has been measured in, and therefore the only ones it can be asked for in.
 *
 * The faded band has to be told apart from the solid band and from the empty track at once, which is a
 * property of the specific hue and not of the component — and at {@link IN_FLIGHT_FADE_CLASS} the
 * dark-theme `brand` and `danger` pairs do not clear the floor `ProgressBar.test.tsx` measures. A
 * documented caveat would not have held: offering a prop combination that renders two bands as one is a
 * trap that fails invisibly, with nothing in the type system, the linter or any test to catch it. So
 * the combination is a compile error instead, and widening this type means taking the measurement.
 */
export type TwoBandTone = 'info';

interface ProgressBarBaseProps {
  /** How much is finished, as a percentage. This is the value the bar reports to assistive tech. */
  progress: number;
  showPercentage?: boolean;
  className?: string;
  size?: 'sm' | 'md' | 'lg';
  /**
   * What is progressing, for a reader who cannot see the bar. Worth passing wherever more than one
   * bar can be on screen at once — a list of uploading files announces three identical "progress"
   * otherwise.
   */
  label?: string;
}

/**
 * One band, in any tone: `info` means here what it means on a badge, and `default` is the neutral one —
 * for a bar that has stopped without anything being wrong, where a status hue would make a deliberate
 * stop look like a fault.
 */
interface SingleBandProps extends ProgressBarBaseProps {
  inFlightProgress?: undefined;
  tone?: StatusTone;
}

interface TwoBandProps extends ProgressBarBaseProps {
  /**
   * How much has been *started*, as a percentage — drawn as a faded band ahead of the solid one, the
   * way a video player draws buffered ahead of played. Omit it and the bar draws a single band.
   *
   * Two real values, never a fake creep: the point of the band is that a bar which sits still for
   * minutes at a time can still show that something is underway. It must therefore be at least
   * `progress` (started is a superset of finished) and is clamped up to it if it is not, since a value
   * behind the fill can only be two reads of a moving pair arriving out of order.
   */
  inFlightProgress: number;
  /**
   * Required here rather than defaulted, which is the point: the default is `brand`, and `brand` is one
   * of the tones a second band has NOT been measured in. See {@link TwoBandTone}.
   */
  tone: TwoBandTone;
}

export type ProgressBarProps = SingleBandProps | TwoBandProps;

function clampPercentage(value: number): number {
  return Math.max(0, Math.min(100, value));
}

export function ProgressBar({
  progress,
  inFlightProgress,
  showPercentage = false,
  className = '',
  size = 'md',
  tone = 'brand',
  label,
}: ProgressBarProps) {
  const clampedProgress = clampPercentage(progress);
  const clampedInFlight =
    inFlightProgress === undefined
      ? null
      : Math.max(clampedProgress, clampPercentage(inFlightProgress));
  const heightClass = size === 'sm' ? 'h-0.5' : size === 'md' ? 'h-1' : 'h-1.5';
  const bandClass = `absolute inset-y-0 left-0 ${fillClass(tone)} rounded-full transition-all duration-300 ease-out`;

  return (
    <div className={`flex items-center gap-2 ${className}`}>
      {/* The track carries the role rather than the wrapper, which also holds the percentage text —
          a bar with no role at all is a state assistive tech cannot read and a test cannot find. */}
      <div
        role="progressbar"
        aria-valuenow={Math.round(clampedProgress)}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={label}
        className={`relative flex-1 ${heightClass} bg-surface rounded-full overflow-hidden`}
      >
        {/* Both bands are absolutely positioned and the solid one comes last, which is what stacks it
            above the faded one: positioned siblings at auto z-index paint in DOM order, and a static
            fill beside an absolute trail would paint UNDER it however they are ordered. The bands take
            their height from the track rather than repeating the size class, so there is one height to
            change and no way for the two to disagree about it. */}
        {clampedInFlight !== null && (
          <div
            data-band="trail"
            aria-hidden="true"
            className={`${bandClass} ${IN_FLIGHT_FADE_CLASS}`}
            style={{ width: `${clampedInFlight}%` }}
          />
        )}
        <div data-band="fill" className={bandClass} style={{ width: `${clampedProgress}%` }} />
      </div>
      {showPercentage && (
        <span className="text-body-sm text-muted min-w-[3rem] text-right">
          {Math.round(clampedProgress)}%
        </span>
      )}
    </div>
  );
}
