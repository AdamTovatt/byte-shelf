import { useLocation } from 'react-router-dom';
import { useCallback } from 'react';

interface IsActiveOptions {
  startsWith?: boolean;
}

export function useIsActive(): (path: string, options?: IsActiveOptions) => boolean {
  const location = useLocation();

  return useCallback((path: string, options?: IsActiveOptions) => {
    if (options?.startsWith) {
      return location.pathname.startsWith(path);
    }
    return location.pathname === path;
  }, [location.pathname]);
}
