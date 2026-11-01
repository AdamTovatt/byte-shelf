import type { ReactNode } from 'react';
import { cn } from '../ui';

interface NoticeProps {
  variant: 'info' | 'danger';
  children: ReactNode;
}

/** A short message inside a form: what was sent, or why it was refused. */
export function Notice({ variant, children }: NoticeProps) {
  return (
    <div
      role={variant === 'danger' ? 'alert' : 'status'}
      className={cn('rounded-lg px-3 py-2 text-body-md', variant === 'danger' ? 'bg-danger-a-3 text-danger-11' : 'bg-info-a-3 text-info-11')}
    >
      {children}
    </div>
  );
}
