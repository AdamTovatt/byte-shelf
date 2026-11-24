import { createContext, useContext, useState, useEffect, useCallback, useRef, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { hasSession, isSessionStorageKey, saveTokens, sessionUser, signOut } from '../services/authService';
import type { AuthResponse } from '../services/authTypes';
import { forgetPasswords } from '../lib/encryption/passwords';

interface AuthContextType {
  isAuthenticated: boolean;
  login: (response: AuthResponse) => void;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  /** Whose session this page shows. */
  const user = useRef(sessionUser());
  // Signed in while a session is kept: an access token that ran out is renewed on the next request.
  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(hasSession);

  // A tab the browser restores keeps its sessionStorage, and with it the passwords, past a sign-out
  // made while it was closed: starting without a session, it forgets them.
  useEffect(() => {
    if (!hasSession()) forgetPasswords();
  }, []);

  // Another tab signing out, or signing in as someone else, changes the shared session. A tab that slept
  // through a sign-out may first see the next sign-in: as someone else's, it starts anew, so that nothing
  // the page holds of the one before, such as a file decrypted to be previewed, is theirs to see.
  useEffect(() => {
    function onStorage(event: StorageEvent): void {
      if (!isSessionStorageKey(event.key)) return;
      queryClient.clear();
      const userChanged = hasSession() && sessionUser() !== user.current;
      if (!hasSession() || userChanged) forgetPasswords();
      if (isAuthenticated && userChanged) window.location.reload();
      setIsAuthenticated(hasSession());
      user.current = sessionUser();
    }

    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, [queryClient, isAuthenticated]);

  const login = useCallback((response: AuthResponse) => {
    // Drop any cached data and passwords from a prior session, so an account never inherits another's.
    queryClient.clear();
    forgetPasswords();
    saveTokens(response);
    user.current = sessionUser();
    setIsAuthenticated(true);
  }, [queryClient]);

  const logout = useCallback(async () => {
    forgetPasswords();
    await signOut();
    setIsAuthenticated(false);
    queryClient.clear();
  }, [queryClient]);

  return (
    <AuthContext.Provider value={{ isAuthenticated, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextType {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
