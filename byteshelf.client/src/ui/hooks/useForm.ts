import { useCallback, useRef, useState, type FormEvent } from 'react';

/**
 * What a field can be told about its value.
 *
 * A string is a message shown under the field. `true` marks the field invalid WITHOUT a message of
 * its own, for a failure that belongs to several fields at once and is said once — a refused login
 * marks both email and password and words it under the password alone.
 */
export type FieldError = string | true;

export type FieldErrors<TValues> = Partial<Record<keyof TValues, FieldError>>;

/** What `onSubmit` is handed to report a failure — the server's refusal, say — back into the form. */
export interface FormSubmitHelpers<TValues> {
  setFieldError: (name: keyof TValues, error: FieldError) => void;
  setFormError: (message: string) => void;
}

export interface UseFormOptions<TValues> {
  initialValues: TValues;
  /**
   * Client-side checks, run on submit before `onSubmit`. Returns the errors to show; an empty object
   * (or no key with a value) lets the submit through.
   */
  validate?: (values: TValues) => FieldErrors<TValues>;
  /** Shown as the form-level error when `onSubmit` throws something that is not an `Error`. */
  fallbackErrorMessage?: string;
  /**
   * Fields whose errors depend on one another's values. Editing a field in a group clears its own
   * error, as any edit does, and re-checks the others: each keeps its error only while `validate`
   * still gives that same error for the new values. So an error about the pair goes as soon as it
   * stops being true, and a field's own rule stays as long as that field still breaks it.
   *
   * A refused login is the first case: the server's message under the password speaks for the email
   * too, and `validate` never gives it, so fixing the email takes it back — while "Enter your
   * password." stays under an empty password. A password and its confirmation are the second: a
   * mismatch said under the confirmation goes once the new password matches it, and typing in the
   * confirmation leaves "too short" standing under a new password that is still too short.
   *
   * An edit never ADDS an error; the reader is told about a broken rule when they submit. Pass a
   * module constant (and a `validate` that is one), or `setValue` is rebuilt on every render.
   */
  linkedErrors?: ReadonlyArray<ReadonlyArray<keyof TValues>>;
}

export interface UseFormResult<TValues> {
  values: TValues;
  /**
   * Sets one value and clears that field's error, since the reader is now answering it; a field
   * linked to it keeps its error only while `validate` still gives it (see `linkedErrors`). It
   * leaves {@link formError} standing.
   */
  setValue: <K extends keyof TValues>(name: K, value: TValues[K]) => void;
  errors: FieldErrors<TValues>;
  /** Whether any field is currently marked. */
  hasErrors: boolean;
  /**
   * A failure that belongs to no one field (a network error, a 5xx) — shown inline in the form.
   *
   * It stays while the reader types, until the next submit clears it: it is a fact about the last
   * attempt, which typing does not undo. A field's error is about a value, which typing does answer.
   */
  formError: string | null;
  isSubmitting: boolean;
  /**
   * Builds the `<form onSubmit>` handler: prevents the navigation, clears the previous attempt's
   * errors, runs `validate`, and then awaits `onSubmit` with `isSubmitting` held.
   *
   * <b>A successful submit stays submitting.</b> Success on every form this was written for
   * navigates away or unmounts the form, and dropping the flag first would re-enable the button for
   * the moment in between — long enough for a second press to send a second request. A submit that
   * arrives while one is in flight is ignored outright, whatever the button says. The flag drops
   * when the attempt FAILED: `onSubmit` threw, or it reported an error through the helpers it is
   * handed (which is how a caller turns a server refusal into a field error).
   *
   * A throw becomes the form-level error: the `Error`'s message, else `fallbackErrorMessage`.
   */
  handleSubmit: (
    onSubmit: (values: TValues, helpers: FormSubmitHelpers<TValues>) => Promise<void> | void,
  ) => (event?: FormEvent) => Promise<void>;
}

const DEFAULT_FALLBACK = 'Something went wrong. Please try again.';

interface FormFields<TValues> {
  values: TValues;
  errors: FieldErrors<TValues>;
}

function hasAnyError<TValues>(errors: FieldErrors<TValues>): boolean {
  return Object.values(errors).some((error) => error !== undefined && error !== '');
}

/**
 * The errors left once `name` has been edited to give `values`: the edited field's own error goes,
 * and a field linked to it keeps its error only while `validate` still gives that same error.
 * Returns `current` itself when nothing changes, so an edit to an unmarked form re-renders nothing
 * on its account.
 */
function errorsAfterEdit<TValues>(
  current: FieldErrors<TValues>,
  name: keyof TValues,
  values: TValues,
  validate: ((values: TValues) => FieldErrors<TValues>) | undefined,
  linkedErrors: ReadonlyArray<ReadonlyArray<keyof TValues>> | undefined,
): FieldErrors<TValues> {
  const linked = (linkedErrors?.find((group) => group.includes(name)) ?? []).filter(
    (field) => field !== name && current[field] !== undefined,
  );
  if (current[name] === undefined && linked.length === 0) return current;

  const next = { ...current };
  delete next[name];
  if (linked.length > 0) {
    const stillGiven: FieldErrors<TValues> = validate?.(values) ?? {};
    for (const field of linked) {
      if (stillGiven[field] !== current[field]) delete next[field];
    }
  }
  return next;
}

/**
 * A small form state hook — values, per-field errors, one form-level error, and a submitting state —
 * so a form can show its feedback IN the fields, in the manner of React Hook Form, without
 * the package: third-party dependencies are kept to a minimum in a validated application, and this
 * is the whole of what the auth forms need. Pair it with `FormField`, which binds a field's value,
 * change handler and error, and `FormError`, which draws the form-level error above the submit.
 */
export function useForm<TValues extends object>({
  initialValues,
  validate,
  fallbackErrorMessage = DEFAULT_FALLBACK,
  linkedErrors,
}: UseFormOptions<TValues>): UseFormResult<TValues> {
  // The values and the field errors are one state, because an edit decides both at once: re-checking
  // a linked field needs the values that edit produced, and an updater handed the current pair has
  // them — however many edits land in one tick before a re-render.
  const [fields, setFields] = useState<FormFields<TValues>>(() => ({ values: initialValues, errors: {} }));
  const { values, errors } = fields;
  const setErrors = useCallback((update: (current: FieldErrors<TValues>) => FieldErrors<TValues>) => {
    setFields((current) => ({ ...current, errors: update(current.errors) }));
  }, []);
  const [formError, setFormError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  // Whether the attempt in flight has reported a failure through its helpers. A ref rather than
  // state because it is read in the same tick `onSubmit` resolves, before any re-render.
  const failedRef = useRef(false);
  // Whether an attempt is in flight, read synchronously so a second submit in the same tick — before
  // `isSubmitting` has re-rendered the button disabled — is refused inside the hook.
  const inFlightRef = useRef(false);

  const setValue = useCallback(<K extends keyof TValues>(name: K, value: TValues[K]) => {
    setFields((current) => {
      const nextValues = { ...current.values, [name]: value };
      return { values: nextValues, errors: errorsAfterEdit(current.errors, name, nextValues, validate, linkedErrors) };
    });
  }, [validate, linkedErrors]);

  const handleSubmit = useCallback<UseFormResult<TValues>['handleSubmit']>(
    (onSubmit) => async (event) => {
      event?.preventDefault();
      event?.stopPropagation();
      if (inFlightRef.current) return;
      setFormError(null);

      const found = validate?.(values) ?? {};
      if (hasAnyError(found)) {
        setErrors(() => found);
        return;
      }
      setErrors(() => ({}));

      failedRef.current = false;
      const helpers: FormSubmitHelpers<TValues> = {
        setFieldError: (name, error) => {
          failedRef.current = true;
          setErrors((current) => ({ ...current, [name]: error }));
        },
        setFormError: (message) => {
          failedRef.current = true;
          setFormError(message);
        },
      };

      inFlightRef.current = true;
      setIsSubmitting(true);
      try {
        await onSubmit(values, helpers);
        // A success stays locked (see the docblock); only a reported failure unlocks.
        if (failedRef.current) {
          inFlightRef.current = false;
          setIsSubmitting(false);
        }
      } catch (error) {
        setFormError(error instanceof Error && error.message ? error.message : fallbackErrorMessage);
        inFlightRef.current = false;
        setIsSubmitting(false);
      }
    },
    [values, validate, fallbackErrorMessage, setErrors],
  );

  return {
    values,
    setValue,
    errors,
    hasErrors: hasAnyError(errors),
    formError,
    isSubmitting,
    handleSubmit,
  };
}
