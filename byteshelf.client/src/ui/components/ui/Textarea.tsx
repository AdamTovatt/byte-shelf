import { type TextareaHTMLAttributes, forwardRef, useCallback, useId, useImperativeHandle, useLayoutEffect, useRef } from 'react';
import { FieldLabel } from './FieldLabel';
import { Squircle } from './Squircle';
import { fieldRendering, type FieldRendering } from './fieldStyles';
import { ErrorText } from './ErrorText';
import { useFieldError } from './useFieldError';

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  /** A message under the field; marks it invalid (danger edge, `aria-invalid`) and describes it. */
  error?: string;
  label?: string;
  /** Renders a red required-marker after the label and sets `aria-required` (no native validation popup). */
  required?: boolean;
  size?: 'sm' | 'md';
  /** Override the inner padding. Defaults to whatever `size` implies. Pass `'none'`
   * when the parent supplies its own padding (e.g. a gutter alongside the textarea). */
  padding?: 'sm' | 'md' | 'none';
  resizable?: boolean;
  autoResize?: boolean;
  variant?: 'default' | 'outline' | 'quote';
  /**
   * How strongly the empty field's placeholder is drawn. `faint` (the default) is a hint that steps back from what is
   * typed; `readable` is the muted ink at full strength, for a box whose placeholder is what invites the first word
   * (a chat box's "Ask about…"), where a faint one read as a box that takes nothing.
   */
  placeholderTone?: 'faint' | 'readable';
}

const placeholderToneClasses = {
  faint: 'placeholder:text-neutral-9/50',
  readable: 'placeholder:text-muted',
};

/**
 * The CSS-drawn variants. `default` is not here: it is the shared squircled field surface
 * ({@link fieldRendering}), the standard field fill, with a grey edge like every other field.
 */
const wrapperVariants = {
  outline: `
    bg-transparent
    border border-border
    focus-within:shadow-focus-glow
  `,
  quote: `
    bg-transparent
  `,
};

const paddingClasses = {
  sm: 'px-3 py-1.5',
  md: 'px-3 py-2',
  none: '',
};

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(
  ({ error, label, required = false, size = 'md', padding, resizable = true, autoResize = false, variant = 'default', placeholderTone = 'faint', className = '', id, onChange, 'aria-describedby': describedBy, 'aria-invalid': ariaInvalid, ...props }, ref) => {
    // Generated rather than made from the label, as Input does: two fields labelled alike on one page
    // would share an id.
    const generatedId = useId();
    const textareaId = id || generatedId;
    const { isInvalid, errorId, controlProps } = useFieldError({ error, describedBy, ariaInvalid });
    const internalRef = useRef<HTMLTextAreaElement | null>(null);

    useImperativeHandle(ref, () => internalRef.current as HTMLTextAreaElement, []);

    const adjustHeight = useCallback(() => {
      const textarea = internalRef.current;
      if (!textarea || !autoResize) return;

      textarea.style.height = 'auto';
      textarea.style.height = `${textarea.scrollHeight}px`;
    }, [autoResize]);

    useLayoutEffect(() => {
      adjustHeight();
    }, [adjustHeight, props.value, props.disabled]);

    // scrollHeight depends on width, but adjustHeight only re-fires on value/disabled
    // changes. Without observing width changes, dragging a parent splitter narrow (or
    // any mount-time width race) measures scrollHeight against the wrong width, locking
    // the textarea at a huge height that survives even after the layout widens again.
    useLayoutEffect(() => {
      if (!autoResize) return;
      const textarea = internalRef.current;
      if (!textarea) return;
      let lastWidth = textarea.clientWidth;
      const observer = new ResizeObserver((entries) => {
        const width = entries[0]?.contentRect.width;
        if (width != null && width !== lastWidth) {
          lastWidth = width;
          adjustHeight();
        }
      });
      observer.observe(textarea);
      return () => observer.disconnect();
    }, [autoResize, adjustHeight]);

    const handleChange = useCallback((event: React.ChangeEvent<HTMLTextAreaElement>) => {
      onChange?.(event);
    }, [onChange]);

    // `default` paints through a Squircle layer; the other variants draw their box with CSS.
    let field: FieldRendering | null = null;
    let wrapperClassName: string;
    if (variant === 'default') {
      field = fieldRendering({ error: isInvalid });
      wrapperClassName = field.className;
    } else {
      wrapperClassName = `relative rounded-lg ${wrapperVariants[variant]} transition-shadow duration-200 ${
        isInvalid ? 'border-danger-7 focus-within:border-danger-7' : 'focus-within:border-neutral-9/50'
      }`;
    }

    const resizeClass = autoResize ? 'resize-none overflow-y-auto' : (resizable ? 'resize-y' : 'resize-none');

    return (
      <div className="flex flex-col gap-2 w-full">
        {label && (
          <FieldLabel htmlFor={textareaId} required={required}>
            {label}
          </FieldLabel>
        )}
        <div className={wrapperClassName}>
          {field && <Squircle {...field.squircle} />}
          <textarea
            ref={internalRef}
            id={textareaId}
            aria-required={required || undefined}
            {...controlProps}
            className={`
              w-full bg-transparent rounded-lg text-fg
              ${placeholderToneClasses[placeholderTone]}
              outline-none border-none
              disabled:opacity-60 disabled:cursor-not-allowed
              ${size === 'sm' ? 'text-sm' : ''}
              ${paddingClasses[padding ?? size]}
              ${resizeClass} ${autoResize ? '' : 'min-h-[100px]'}
              ${className}
            `}
            onChange={handleChange}
            {...props}
          />
        </div>
        {error && (
          <ErrorText id={errorId}>{error}</ErrorText>
        )}
      </div>
    );
  }
);

Textarea.displayName = 'Textarea';
