import type { ReactNode } from 'react';
import { PageLayout } from '../ui';
import { LoadError } from './LoadError';
import { useCurrentUser } from '../hooks/useUserQueries';
import { isAdmin } from '../services/userService';

/**
 * Draws `children` with whether the signed-in user is an admin, once that is known: nothing while
 * it loads, and why when it could not be loaded, so a failed load never reads as "not an admin" and
 * sends an admin away from a page that is theirs.
 */
export function KnownRole({ children }: { children: (isAdmin: boolean) => ReactNode }) {
  const { data: me, error } = useCurrentUser();
  if (me) return children(isAdmin(me));
  if (!error) return null;
  return (
    <PageLayout>
      <LoadError error={error} failure="Could not load who you are" />
    </PageLayout>
  );
}
