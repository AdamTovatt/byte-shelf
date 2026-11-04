export interface SpinnerProps {
  size?: 'sm' | 'md' | 'lg';
  /**
   * Where the ring's colour comes from. `fg` is the primary ink and the default; `inherit` takes the
   * surrounding text's colour, for a spinner on a filled button or inside a coloured line. A prop
   * rather than a `className`, because a caller's `text-*` would compete with `text-fg` at equal
   * specificity and win or lose by Tailwind's emission order.
   */
  tone?: 'fg' | 'inherit';
  className?: string;
}

const sizeStyles: Record<NonNullable<SpinnerProps['size']>, string> = {
  sm: 'h-3.5 w-3.5',
  md: 'h-5 w-5',
  lg: 'h-8 w-8',
};

// `inherit` sets no colour: `color` is inherited, and the ring draws in `currentColor`.
const toneStyles: Record<NonNullable<SpinnerProps['tone']>, string> = {
  fg: 'text-fg',
  inherit: '',
};

/**
 * A loading indicator: a quarter arc turning on a faint ring. Reduced motion holds it still (see
 * `styles.css`). `data-spinner` is the stable hook for a test that needs to find one.
 */
export function Spinner({ size = 'md', tone = 'fg', className = '' }: SpinnerProps) {
  return (
    <svg
      data-spinner=""
      role="status"
      aria-label="Loading"
      className={`spinner ${toneStyles[tone]} ${sizeStyles[size]} ${className}`}
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 24 24"
      fill="none"
    >
      <circle cx="12" cy="12" r="9" stroke="currentColor" strokeOpacity="0.2" strokeWidth="3" />
      <path d="M21 12a9 9 0 0 0-9-9" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
    </svg>
  );
}
