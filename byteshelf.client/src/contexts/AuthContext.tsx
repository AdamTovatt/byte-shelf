import { createContext, useContext, useState, useEffect, useCallback, useRef, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { hasSession, isSessionStorageKey, onSessionEnded, saveTokens, sessionUser, signOut } from '../services/authService';
import type { AuthResponse } from '../services/authTypes';
import { forgetPasswords } from '../lib/encryption/passwords';

interface AuthContextType {
  isAuthenticated: boolean;
  login: (response: AuthResponse) => void;
  /** Signs out and starts the page anew. */
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

  /** Takes up the session there is now, if any, with nothing cached or kept of the one before. */
  const reset = useCallback(() => {
    forgetPasswords();
    queryClient.clear();
    user.current = sessionUser();
    setIsAuthenticated(hasSession());
  }, [queryClient]);

  // For a session that has ended: nothing the page holds of it, such as a file decrypted to be previewed
  // or what a plugin keeps in memory, is there for whoever signs in next.
  const startAnew = useCallback(() => {
    reset();
    window.location.reload();
  }, [reset]);

  useEffect(() => {
    // Another tab signing out or in, as whoever it is, changes the shared session. A tab that slept
    // through a sign-out may first see the next sign-in. A renewal leaves the session as it is.
    function onStorage(event: StorageEvent): void {
      if (!isSessionStorageKey(event.key)) return;
      if (!isAuthenticated) reset();
      else if (!hasSession() || sessionUser() !== user.current) startAnew();
    }

    const stopListening = onSessionEnded(() => isAuthenticated && startAnew());
    window.addEventListener('storage', onStorage);
    return () => {
      stopListening();
      window.removeEventListener('storage', onStorage);
    };
  }, [isAuthenticated, reset, startAnew]);

  const login = useCallback((response: AuthResponse) => {
    saveTokens(response);
    reset();
  }, [reset]);

  const logout = useCallback(async () => {
    await signOut();
    startAnew();
  }, [startAnew]);

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
