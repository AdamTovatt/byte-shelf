import type { Ref } from 'react';
import { Input, type InputProps } from './Input';
import type { UseFormResult } from '../../hooks/useForm';

/** Keys of the form whose value is a string — the only kind a text field can hold. */
type StringKeys<TValues> = { [K in keyof TValues]: TValues[K] extends string ? K : never }[keyof TValues];

export interface FormFieldProps<TValues extends object>
  // `form` is left out as well as the bound props: it is the native `<input form="…">` attribute on
  // `InputProps`, and here it names the hook's form instead.
  extends Omit<InputProps, 'value' | 'onChange' | 'error' | 'invalid' | 'name' | 'form'> {
  /** The form this field belongs to, from {@link useForm}. */
  form: UseFormResult<TValues>;
  /** Which value it edits. */
  name: StringKeys<TValues>;
  ref?: Ref<HTMLInputElement>;
}

/**
 * An `Input` bound to one value of a {@link useForm} form: its value, its change handler (which clears
 * the field's error as the reader answers it) and its error, drawn by `Input`'s own `error` — so the
 * label, the message under the field and the `aria-describedby` between them are wired once here
 * rather than at every call site (#2455). The message appears only while there is one, and moves
 * what sits below it down.
 */
export function FormField<TValues extends object>({ form, name, disabled, ref, ...inputProps }: FormFieldProps<TValues>) {
  const error = form.errors[name];
  return (
    <Input
      ref={ref}
      {...inputProps}
      name={String(name)}
      value={form.values[name] as string}
      onChange={(event) => form.setValue(name, event.target.value as TValues[typeof name])}
      // A string is said under the field; `true` marks it without words of its own (see `FieldError`
      // in useForm).
      error={typeof error === 'string' ? error : undefined}
      invalid={error === true}
      // A field is locked while its form submits, whatever the caller also asks for.
      disabled={disabled || form.isSubmitting}
    />
  );
}
