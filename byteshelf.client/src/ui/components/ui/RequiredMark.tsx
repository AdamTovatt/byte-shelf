export interface RequiredMarkProps {
  className?: string;
}

/**
 * Red asterisk marking a form field as required. Decorative only (`aria-hidden`) — the field
 * itself carries `aria-required`/native `required` so assistive tech announces the state. Kept as a
 * single component so the marker's colour and spacing can't drift between the inputs that use it.
 */
export function RequiredMark({ className = '' }: RequiredMarkProps) {
  return (
    <span aria-hidden="true" className={`text-danger-11 ml-0.5 ${className}`}>
      *
    </span>
  );
}
