import { useCallback, useState } from 'react';
import { getErrorMessage } from '../lib/utils';

/**
 * The submit step forms and dialogs share: `submit` runs an action, then `onDone` with its result; a
 * refusal is kept as `error`, the server's reason or `failure`, to show under the fields.
 */
export function useSubmit(failure: string) {
  const [error, setError] = useState<string>();

  const submit = useCallback(
    async <T>(action: () => Promise<T>, onDone?: (result: T) => void): Promise<void> => {
      let result: T;
      try {
        result = await action();
      } catch (caught) {
        setError(getErrorMessage(caught, failure));
        return;
      }

      setError(undefined);
      onDone?.(result);
    },
    [failure],
  );

  return { error, setError, submit };
}
