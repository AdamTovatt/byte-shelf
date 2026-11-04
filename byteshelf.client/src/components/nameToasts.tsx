import toast from 'react-hot-toast';
import { getErrorMessage } from '../ui';
import { NameAndReason } from './MiddleTruncatedName';

/** What a refusal caught says, or a stand-in when it says nothing. */
export function reasonOf(caught: unknown): string {
  return getErrorMessage(caught, 'it was refused');
}

/** Shows that something named failed: `before` and the name on one line, the reason below it. */
export function toastNamedFailure(before: string, name: string, reason: string): void {
  toast.error(<NameAndReason before={before} name={name} reason={reason} />);
}

/**
 * Runs an action on something named, started outside a form such as from a row's menu, and shows a
 * refusal as a toast naming it: "Could not encrypt reports:", then the reason.
 */
export async function runOrToastNamed(action: () => Promise<unknown>, before: string, name: string): Promise<void> {
  try {
    await action();
  } catch (caught) {
    toastNamedFailure(before, name, reasonOf(caught));
  }
}
