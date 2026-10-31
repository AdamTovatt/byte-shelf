import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { hasSession, isSessionStorageKey, saveTokens, signOut } from '../services/authService';
import type { AuthResponse } from '../services/authTypes';

interface AuthContextType {
  isAuthenticated: boolean;
  login: (response: AuthResponse) => void;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  // Signed in while a session is kept: an access token that ran out is renewed on the next request.
  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(hasSession);

  // Another tab signing out, or signing in as someone else, changes the shared session.
  useEffect(() => {
    function onStorage(event: StorageEvent): void {
      if (isSessionStorageKey(event.key)) {
        queryClient.clear();
        setIsAuthenticated(hasSession());
      }
    }

    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, [queryClient]);

  const login = useCallback((response: AuthResponse) => {
    // Drop any cached data from a prior session, so an account never inherits another's queries.
    queryClient.clear();
    saveTokens(response);
    setIsAuthenticated(true);
  }, [queryClient]);

  const logout = useCallback(async () => {
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
