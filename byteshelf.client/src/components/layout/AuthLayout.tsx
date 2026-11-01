import { type ReactNode } from 'react';
import { Card } from '../../ui';
import { Logo } from '../Logo';

interface AuthLayoutProps {
  /** The heading shown under the logo, e.g. the app name or the flow's purpose. */
  title: string;
  children: ReactNode;
}

/**
 * The shared full-screen frame for unauthenticated pages (login, forgot password, reset):
 * centered logo + heading above a single card that hosts the page's form.
 */
export function AuthLayout({ title, children }: AuthLayoutProps) {
  return (
    <div className="min-h-screen flex items-center justify-center bg-canvas text-fg p-6 sm:p-8">
      <div className="w-full max-w-md">
        <div className="flex flex-col items-center gap-4 mb-8">
          <Logo alt="ByteShelf" className="h-20 w-20 rounded-2xl shadow-lg" />
          <h1 className="text-heading-lg text-fg">{title}</h1>
        </div>

        <Card variant="elevated" padding="lg" className="animate-scale-in">
          {children}
        </Card>
      </div>
    </div>
  );
}
