import { Button, getErrorMessage } from '../../ui';
import { PreviewMessage } from './PreviewMessage';

type PreviewFailureProps = { onTryAgain: () => void } & (
  /** Why the file could not be read. */
  | { error: unknown; message?: never }
  /** What is said when that is not it. */
  | { message: string; error?: never }
);

/** Why a file is not shown, which trying again may mend. */
export function PreviewFailure({ error, message = getErrorMessage(error, 'Could not read the file.'), onTryAgain }: PreviewFailureProps) {
  return <PreviewMessage message={message} action={<Button variant="secondary" onClick={onTryAgain}>Try again</Button>} />;
}
