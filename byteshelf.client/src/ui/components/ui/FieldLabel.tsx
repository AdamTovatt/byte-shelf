import { RequiredMark } from './RequiredMark';

export interface FieldLabelProps {
  /** The control this labels (the input/textarea `id`). */
  htmlFor?: string;
  /** Renders the red required-marker beside the label. The field itself carries `aria-required`. */
  required?: boolean;
  /** Override the label text styling. Defaults to the shared field-label style. */
  className?: string;
  children: string;
}

const DEFAULT_LABEL_CLASS = 'text-body-md font-medium text-fg';

/**
 * The label row shared by the form fields (`Input`, `Textarea`). The required-marker sits *beside*
 * the `<label>`, never inside it, so the label's text content stays clean — the field's required
 * state is conveyed by `aria-required` on the control, and `getByLabelText('…')` keeps matching the
 * plain label. Centralised here so that invariant can't drift between fields.
 */
export function FieldLabel({ htmlFor, required = false, className, children }: FieldLabelProps) {
  return (
    <div className="flex items-center">
      <label htmlFor={htmlFor} className={className || DEFAULT_LABEL_CLASS}>
        {children}
      </label>
      {required && <RequiredMark />}
    </div>
  );
}
