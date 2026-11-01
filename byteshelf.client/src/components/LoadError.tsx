import { ErrorText, getErrorMessage } from '../ui';

interface LoadErrorProps {
  /** The query's error; nothing is drawn without one. */
  error: Error | null;
  /** Shown when the error carries no message of its own. */
  failure: string;
}

/** Why something on the page could not be loaded, so a failed load does not read as an empty list. */
export function LoadError({ error, failure }: LoadErrorProps) {
  return <ErrorText role="alert">{error && getErrorMessage(error, failure)}</ErrorText>;
}
