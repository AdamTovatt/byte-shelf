import type { ReactNode } from 'react';
import toast from 'react-hot-toast';
import { Button } from '../ui';

interface ActionToast {
  message: ReactNode;
  /** The button's label. */
  action: string;
  icon?: ReactNode;
  /** Runs once the button is pressed, after the toast is dismissed. */
  onAction: () => void;
  duration: number;
}

/**
 * A toast of a message and one button, such as Undo, gone once the button is pressed. A name in the
 * message goes in a NameLine.
 */
export function showActionToast({ message, action, icon, onAction, duration }: ActionToast): void {
  toast((t) => (
    // The button keeps its whole label on one line; the message takes what is left, its name cut to fit.
    <span className="flex w-full min-w-0 items-center gap-3">
      <span className="min-w-0 flex-1">{message}</span>
      <Button
        size="sm"
        variant="secondary"
        icon={icon}
        className="shrink-0 whitespace-nowrap"
        onClick={() => {
          toast.dismiss(t.id);
          onAction();
        }}
      >
        {action}
      </Button>
    </span>
  ), { duration });
}
