import { createContext, useContext, useState, useEffect, useLayoutEffect, type ReactNode } from 'react';

export type Theme = 'system' | 'light' | 'dark';
export type ResolvedTheme = 'light' | 'dark';

const STORABLE_THEMES: readonly Theme[] = ['system', 'light', 'dark'];

interface ThemeContextType {
  theme: Theme;
  resolvedTheme: ResolvedTheme;
  /** The OS-level preference (light or dark), independent of the user's chosen theme. */
  systemPreference: ResolvedTheme;
  setTheme: (theme: Theme) => void;
}

const ThemeContext = createContext<ThemeContextType | undefined>(undefined);

const THEME_STORAGE_KEY = 'byteshelf-theme';

function getSystemPreference(): ResolvedTheme {
  if (typeof window === 'undefined') {
    return 'light';
  }
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function getStoredTheme(): Theme | null {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    if (stored != null && (STORABLE_THEMES as readonly string[]).includes(stored)) {
      return stored as Theme;
    }
  } catch {
    // Storage blocked (private mode, quota): fall back to the default.
  }
  return null;
}

/**
 * The chosen theme, applied to <html> as `data-theme` (which the colour variables follow) and as the
 * `dark` class (which Tailwind's `dark:` variant follows). "System" follows the OS as it changes.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(() => getStoredTheme() ?? 'system');
  const [systemPreference, setSystemPreference] = useState<ResolvedTheme>(getSystemPreference);

  const resolvedTheme: ResolvedTheme = theme === 'system' ? systemPreference : theme;

  useLayoutEffect(() => {
    const root = document.documentElement;
    root.dataset.theme = resolvedTheme;
    root.classList.toggle('dark', resolvedTheme === 'dark');
  }, [resolvedTheme]);

  useEffect(() => {
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    const handleChange = () => setSystemPreference(getSystemPreference());
    mediaQuery.addEventListener('change', handleChange);
    return () => mediaQuery.removeEventListener('change', handleChange);
  }, []);

  const setTheme = (newTheme: Theme): void => {
    setThemeState(newTheme);
    try {
      localStorage.setItem(THEME_STORAGE_KEY, newTheme);
    } catch {
      // Storage blocked: the choice holds for this page only.
    }
  };

  return (
    <ThemeContext.Provider value={{ theme, resolvedTheme, systemPreference, setTheme }}>
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme(): ThemeContextType {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error('useTheme must be used within a ThemeProvider');
  }
  return context;
}
