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

/** A toast of a message and one button, such as Undo, gone once the button is pressed. */
export function showActionToast({ message, action, icon, onAction, duration }: ActionToast): void {
  toast((t) => (
    <span className="flex items-center gap-3">
      <span className="min-w-0 truncate">{message}</span>
      <Button
        size="sm"
        variant="secondary"
        icon={icon}
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
