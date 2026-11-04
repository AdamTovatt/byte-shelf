import toast from 'react-hot-toast';
import { getErrorMessage } from '../ui';

/** Runs an action started outside a form, such as a row's button, and shows a refusal as a toast. */
export async function runOrToast(action: () => Promise<unknown>, failure: string): Promise<void> {
  try {
    await action();
  } catch (caught) {
    toast.error(getErrorMessage(caught, failure));
  }
}
