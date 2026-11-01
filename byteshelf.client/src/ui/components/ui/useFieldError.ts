import { type AriaAttributes, useId } from 'react';

export interface FieldErrorOptions {
  /** The refusal drawn under the field. Marks it invalid and describes it. */
  error?: string;
  /** Marks the field invalid with no message of its own. Implied by {@link error}. */
  invalid?: boolean;
  /** The caller's own `aria-describedby`, which the message joins rather than replaces. */
  describedBy?: string;
  /** The caller's own `aria-invalid`, which stands only while the component has no refusal to state. */
  ariaInvalid?: AriaAttributes['aria-invalid'];
}

/**
 * What {@link useFieldError} hands a field: whether it looks refused, its message's id, and the two
 * ARIA attributes for its control.
 */
export interface FieldErrorWiring {
  /** Whether the field draws its refused look (the danger edge). */
  isInvalid: boolean;
  /** The id to put on the `ErrorText` under the field. */
  errorId: string;
  /**
   * The two attributes for the element that carries the control's role and focus. Both are resolved
   * here from the caller's values, so a caller's rest props cannot overwrite them by spread order.
   */
  controlProps: {
    'aria-invalid': AriaAttributes['aria-invalid'];
    'aria-describedby': string | undefined;
  };
}

/**
 * How a field states a refusal to assistive technology — `Input`, `Textarea` and `Select` alike, so
 * the three cannot drift into copies that disagree.
 *
 * - The message's id JOINS whatever the caller already described the field with, so a screen reader
 *   hears both the caller's hint and why the value was refused.
 * - A refusal WINS over a caller's `aria-invalid`: a field drawing a danger edge and a message cannot
 *   also announce itself valid. With no refusal, the caller's value passes through untouched.
 */
export function useFieldError({ error, invalid = false, describedBy, ariaInvalid }: FieldErrorOptions): FieldErrorWiring {
  const errorId = useId();
  const isInvalid = Boolean(error) || invalid;
  const describedByIds = [describedBy, error ? errorId : undefined].filter(Boolean).join(' ') || undefined;
  return {
    isInvalid,
    errorId,
    controlProps: {
      'aria-invalid': isInvalid ? true : ariaInvalid,
      'aria-describedby': describedByIds,
    },
  };
}
