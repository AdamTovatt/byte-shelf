import type { ImgHTMLAttributes } from 'react';

/**
 * The circle's diameter, as a rung rather than a number.
 *
 * <b>`xs+` is 28px, between `xs` (24) and `sm` (32), and its NAME is the load-bearing part.</b> The
 * ordinal names were all taken when it was needed, so every alternative spelling claimed a position
 * that already belonged to another rung: `2xs` reads as smaller than `xs` by convention, and `sm-`
 * reads as `sm` having moved. Either would invite the one change that must not happen here —
 * renumbering the scale silently resizes every existing caller, and `xs` has them (a comment
 * thread's avatars, the component showcase). `xs+` says the only true thing: one step above `xs`,
 * with `xs` and `sm` exactly where they were.
 *
 * A semantic rung (`row`, `compact`) was the alternative considered, and it has a real argument —
 * it would stop the scale being subdivided again the next time a caller wants an in-between size.
 * Rejected because it puts two naming systems in one union, so `size` would mean a magnitude in
 * four places and a role in the fifth, and nothing would say which a new rung should be.
 */
type AvatarSize = 'xs' | 'xs+' | 'sm' | 'md' | 'lg' | 'xl';
type AvatarStatus = 'online' | 'offline' | 'busy';

export interface AvatarProps extends Omit<ImgHTMLAttributes<HTMLImageElement>, 'size'> {
  size?: AvatarSize;
  name?: string;
  status?: AvatarStatus;
}

const sizeStyles: Record<AvatarSize, string> = {
  // `text-body-xs`, the house token, where the rungs below still take Tailwind's own scale. At 24px
  // the circle is small enough that the two scales part company: `text-xs` is 12px in a 24px circle,
  // which leaves a pair of initials all but touching its edge. One rung migrated rather than five,
  // because the others are read at sizes where the stock scale is fine and each has its own callers.
  xs: 'w-6 h-6 text-body-xs',
  // The same 10px token as `xs`, deliberately: this rung exists to make the CIRCLE bigger in a row
  // that had space for it, not the initials. Two letters at 10px in a 28px circle simply sit with
  // more room around them, which is the whole of the change a reader sees.
  'xs+': 'w-7 h-7 text-body-xs',
  sm: 'w-8 h-8 text-sm',
  md: 'w-10 h-10 text-base',
  lg: 'w-12 h-12 text-lg',
  xl: 'w-16 h-16 text-xl',
};

const statusSizeStyles: Record<AvatarSize, string> = {
  xs: 'w-2 h-2 border',
  // UNEXERCISED: no caller passes `status` at this rung, and this entry exists because the record is
  // keyed by the whole scale. Chosen rather than measured — the dot follows the circle up to 10px
  // while keeping `xs`'s hairline ring — so treat it as a starting point a first caller should look
  // at, not as a value anything has looked at.
  'xs+': 'w-2.5 h-2.5 border',
  sm: 'w-2.5 h-2.5 border-[1.5px]',
  md: 'w-3 h-3 border-2',
  lg: 'w-3.5 h-3.5 border-2',
  xl: 'w-4 h-4 border-2',
};

const statusColorStyles: Record<AvatarStatus, string> = {
  online: 'bg-success-9',
  offline: 'bg-muted',
  busy: 'bg-danger-9',
};

function getInitials(name: string): string {
  return name
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0].toUpperCase())
    .join('');
}

export function Avatar({
  size = 'md',
  name,
  status,
  src,
  alt,
  className = '',
  ...props
}: AvatarProps) {
  const initials = name ? getInitials(name) : '';

  return (
    <div className={`relative inline-flex shrink-0 ${className}`}>
      {src ? (
        <img
          src={src}
          alt={alt || name || 'Avatar'}
          className={`${sizeStyles[size]} rounded-full object-cover ring-1 ring-border/50`}
          {...props}
        />
      ) : (
        <div
          className={`${sizeStyles[size]} rounded-full flex items-center justify-center font-medium bg-brand-surface text-brand-tint`}
        >
          {initials}
        </div>
      )}
      {status && (
        <span
          className={`absolute bottom-0 right-0 rounded-full border-panel ${statusSizeStyles[size]} ${statusColorStyles[status]}`}
        />
      )}
    </div>
  );
}
