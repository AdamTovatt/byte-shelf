import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { cn } from '../../lib/utils';

export interface ClickableRowProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  children: ReactNode;
  /**
   * Whether the button fills its container (`'full'`, the default) or is only as wide as what it
   * contains (`'fit'`).
   *
   * A prop rather than something a caller passes through `className`, because `cn` here is plain
   * string concatenation and not `tailwind-merge`: `className="w-auto"` emits `w-full w-auto` and
   * which one wins is CSS source order rather than the order they were written. So a width written
   * that way is not an override, it is a coin toss that happens to land the same way every time
   * until a stylesheet is reordered.
   *
   * `'fit'` is for a button wrapped around something with a size of its own — a picture, a chip —
   * where a full-width target means clicking the empty space beside the thing still activates it.
   *
   * It decides WIDTH only. `text-left` applies either way: a button's centred default is wrong for
   * everything this component is used for, and making it ride along with the width would mean a
   * caller that narrowed the target silently got its text re-aligned as well.
   */
  width?: 'full' | 'fit';
}

export function ClickableRow({ children, className, type = 'button', width = 'full', ...props }: ClickableRowProps) {
  return (
    <button
      type={type}
      className={cn(
        'text-left',
        width === 'full' ? 'w-full' : 'w-fit',
        'transition-colors cursor-pointer group',
        className,
      )}
      {...props}
    >
      {children}
    </button>
  );
}
