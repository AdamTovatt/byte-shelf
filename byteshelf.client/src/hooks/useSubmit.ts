import { useCallback, useState } from 'react';
import { getErrorMessage } from '../ui';

/**
 * The submit step forms and dialogs share: `submit` runs an action, then `onDone` with its result; a
 * refusal is kept as `error`, the server's reason or `failure`, to show under the fields. `submitting`
 * is true while the action runs, for a form whose action is not a mutation with its own `isPending`.
 */
export function useSubmit(failure: string) {
  const [error, setError] = useState<string>();
  const [submitting, setSubmitting] = useState(false);

  const submit = useCallback(
    async <T>(action: () => Promise<T>, onDone?: (result: T) => void): Promise<void> => {
      let result: T;
      setSubmitting(true);
      try {
        result = await action();
      } catch (caught) {
        setError(getErrorMessage(caught, failure));
        return;
      } finally {
        setSubmitting(false);
      }

      setError(undefined);
      onDone?.(result);
    },
    [failure],
  );

  return { error, setError, submitting, submit };
}
