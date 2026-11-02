import { type ReactNode } from 'react';
import { Card } from '../../ui';
import { Logo } from '../Logo';

interface AuthLayoutProps {
  /** The heading under the logo, saying what the page is for. Without one the logo is the heading. */
  title?: string;
  children: ReactNode;
}

/**
 * The shared full-screen frame for unauthenticated pages (login, forgot password, reset):
 * centered logo, and a heading when the page has one, above a single card that hosts the page's form.
 */
export function AuthLayout({ title, children }: AuthLayoutProps) {
  const logo = <Logo alt="ByteShelf" className="h-20 w-20 rounded-2xl shadow-lg" />;

  return (
    <div className="min-h-screen flex items-center justify-center bg-canvas text-fg p-6 sm:p-8">
      <div className="w-full max-w-md">
        <div className="flex flex-col items-center gap-4 mb-8">
          {title ? (
            <>
              {logo}
              <h1 className="text-heading-lg text-fg">{title}</h1>
            </>
          ) : (
            // The page's title for a screen reader, read from the logo's name.
            <h1>{logo}</h1>
          )}
        </div>

        <Card variant="elevated" padding="lg" className="animate-scale-in">
          {children}
        </Card>
      </div>
    </div>
  );
}
