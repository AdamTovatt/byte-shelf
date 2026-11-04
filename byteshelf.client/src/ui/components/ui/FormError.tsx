import { ErrorText } from './ErrorText';

export interface FormErrorProps {
  /** The form-level error, or nothing to draw no message. */
  message: string | null | undefined;
  className?: string;
}

/**
 * A form's own failure — one that belongs to no single field (a network error, a 5xx, a rate limit) —
 * drawn inline in the form, under its last field, in the words a field's message uses, and only
 * while there is one.
 *
 * Deliberately not an `Alert` card: the form's feedback lives in the form, the way a field's does,
 * rather than in a banner a reader has to connect back to the button they pressed. `role="alert"` so
 * a screen reader announces it when it appears.
 */
export function FormError({ message, className = '' }: FormErrorProps) {
  return (
    <ErrorText role="alert" testId="form-error" className={className}>
      {message}
    </ErrorText>
  );
}
