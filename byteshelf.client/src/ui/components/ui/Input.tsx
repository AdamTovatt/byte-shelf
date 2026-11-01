import { type InputHTMLAttributes, type ReactNode, forwardRef, useId, useLayoutEffect, useState } from 'react';
import { FieldLabel } from './FieldLabel';
import { Squircle } from './Squircle';
import { fieldRendering } from './fieldStyles';
import { ErrorText } from './ErrorText';
import { FormError } from './FormError';
import { useFieldError } from './useFieldError';

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  /** A message under the field; marks it invalid (danger edge, `aria-invalid`) and describes it. */
  error?: string;
  /**
   * Marks the field invalid with NO message of its own — for a failure said once for several fields,
   * such as a refused login, whose words sit under the password while the email is marked too.
   * Implied by {@link error}.
   */
  invalid?: boolean;
  /**
   * The form's own failure — one that belongs to no single field — said under this field while it
   * has no {@link error} of its own, for a form's LAST field, so it appears where the field's own
   * message would (#2455). It is the form's line, not this field's: it marks nothing, describes
   * nothing, and is announced and found as `FormError`. The field's own error takes the line back the
   * moment there is one.
   */
  formError?: string | null;
  label?: string;
  /** Renders a red required-marker after the label and sets `aria-required` (no native validation popup). */
  required?: boolean;
  labelClassName?: string;
  /** Extra classes for the bordered field wrapper — e.g. a fixed `h-10` to match adjacent controls. */
  wrapperClassName?: string;
  icon?: ReactNode;
  /**
   * Drawn inside the field at its right end — a clear ✕, a spinner, a filter control. The text stops short of it:
   * by the room one icon button takes, or by its measured width where it is wider, such as a chip beside a button.
   */
  endElement?: ReactNode;
  /**
   * `default` glows on focus; `quiet` is the same field without the glow, for a field that should not
   * draw attention when entered (a list filter).
   */
  variant?: 'default' | 'quiet';
  /** Rest on a card's shadow, the focus glow taking its place under focus. */
  raised?: boolean;
}

/**
 * Whether each variant glows on focus. Both are drawn by the shared squircled field surface
 * ({@link fieldRendering}) rather than by CSS classes on the wrapper.
 */
const FIELD_GLOW: Record<NonNullable<InputProps['variant']>, boolean> = {
  default: true,
  quiet: false,
};

/**
 * The `<input>`'s own classes — radius, padding, ink — and the reason the last two moved here from
 * the shared base: a variant has to be able to state its own box and its own colour, and it cannot
 * do either from a call site. `cn` concatenates rather than merges, so a `px-*` or `text-*` passed
 * as `className` does not override the base's, it joins it, and stylesheet order rather than
 * argument order decides which wins — a coin toss that lands the same way until Tailwind emits the
 * two in a different order. The table already decided the radius, so deciding the rest of the box
 * beside it is where that decision already lived.
 *
 * The app variants share one box: they differ only in the focus glow ({@link FIELD_GLOW}), which
 * the layer draws, not the `<input>`. Their `rounded-lg` is not the corner a reader sees — the
 * Squircle layer paints that — but it does shape the autofill tint a browser paints on the input
 * itself (`index.css`).
 */
const APP_INPUT_CLASS = 'rounded-lg px-3 py-2 text-fg placeholder:text-neutral-9/50';

const inputVariants = {
  default: APP_INPUT_CLASS,
  quiet: APP_INPUT_CLASS,
};

export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ error, invalid = false, formError, label, required = false, labelClassName, wrapperClassName = '', icon, endElement, variant = 'default', raised = false, className = '', id, 'aria-describedby': describedBy, 'aria-invalid': ariaInvalid, ...props }, ref) => {
    // Generated rather than made from the label: two fields labelled alike on one page (a form and a
    // dialog over it) would share an id, and the dialog's label would name the field behind it.
    const generatedId = useId();
    const inputId = id || generatedId;
    const { isInvalid, errorId, controlProps } = useFieldError({ error, invalid, describedBy, ariaInvalid });
    const [endElementBox, setEndElementBox] = useState<HTMLDivElement | null>(null);
    const endPaddingRight = useEndElementPadding(endElementBox);

    // Both variants paint through a Squircle layer: fill, edge, focus glow and error edge alike.
    const field = fieldRendering({ error: isInvalid, glow: FIELD_GLOW[variant], raised });

    return (
      // `data-field-block` marks the whole block — label, field and error line — for a test measuring it.
      <div className="flex flex-col gap-2 w-full" data-field-block="">
        {label && (
          <FieldLabel htmlFor={inputId} required={required} className={labelClassName}>
            {label}
          </FieldLabel>
        )}
        <div
          className={`
            ${field.className}
            ${wrapperClassName}
          `}
        >
          <Squircle {...field.squircle} />
          {icon && (
            <div className="absolute left-4 top-1/2 -translate-y-1/2 z-10 text-muted pointer-events-none">
              {icon}
            </div>
          )}
          <input
            ref={ref}
            id={inputId}
            aria-required={required || undefined}
            {...controlProps}
            className={`
              w-full ${inputVariants[variant]}
              bg-transparent
              outline-none border-none
              disabled:opacity-40 disabled:cursor-not-allowed
              ${icon ? 'pl-12' : ''}
              ${className}
            `}
            {...props}
            style={endElement ? { ...props.style, paddingRight: endPaddingRight } : props.style}
          />
          {endElement && (
            <div ref={setEndElementBox} className="absolute right-2 top-1/2 -translate-y-1/2 z-10">
              {endElement}
            </div>
          )}
        </div>
        {error || !formError
          ? <ErrorText id={errorId}>{error}</ErrorText>
          : <FormError message={formError} className="animate-fade-in" />}
      </div>
    );
  }
);

Input.displayName = 'Input';

/** The least room the text keeps for an end element: one icon button and the inset either side of it. */
const END_ELEMENT_MIN_PADDING_PX = 48;
/** The end element's inset from the field's right edge (`right-2`) and the gap the text keeps before it. */
const END_ELEMENT_GAP_PX = 8;

/**
 * The input's right padding for its end element, measured as it is drawn — so a chip and a button sit clear of
 * the text however long the chip's words are — and never less than one icon button's room, which is all it is
 * until measured and where nothing measures (a test's DOM).
 */
function useEndElementPadding(box: HTMLDivElement | null): number {
  const [paddingRight, setPaddingRight] = useState(END_ELEMENT_MIN_PADDING_PX);
  useLayoutEffect(() => {
    if (!box || typeof ResizeObserver === 'undefined') return;
    const read = () => setPaddingRight(Math.max(END_ELEMENT_MIN_PADDING_PX, box.offsetWidth + 2 * END_ELEMENT_GAP_PX));
    read();
    const observer = new ResizeObserver(read);
    observer.observe(box);
    return () => observer.disconnect();
  }, [box]);
  return paddingRight;
}
