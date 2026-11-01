import { createContext, useContext, useState, useCallback, useMemo, type ReactNode } from 'react';
import { readStoredValue, writeStoredValue } from '../lib/storage';
import { SIDEBAR_DEFAULT_WIDTH, SIDEBAR_MAX_WIDTH, SIDEBAR_MIN_WIDTH } from '../ui';

interface SidebarContextValue {
  isCollapsed: boolean;
  setIsCollapsed: (collapsed: boolean) => void;
  toggleCollapsed: () => void;
  sidebarWidth: number;
  setSidebarWidth: (width: number) => void;
  isMobileOpen: boolean;
  setIsMobileOpen: (open: boolean) => void;
}

const SidebarContext = createContext<SidebarContextValue | undefined>(undefined);

const COLLAPSED_STORAGE_KEY = 'sidebar-collapsed';
const WIDTH_STORAGE_KEY = 'sidebar-width';

function getInitialCollapsed(): boolean {
  return readStoredValue(COLLAPSED_STORAGE_KEY) === 'true';
}

function getInitialWidth(): number {
  const stored = readStoredValue(WIDTH_STORAGE_KEY);
  if (stored !== null) {
    const parsed = Number(stored);
    if (!Number.isNaN(parsed) && parsed >= SIDEBAR_MIN_WIDTH && parsed <= SIDEBAR_MAX_WIDTH) {
      return parsed;
    }
  }
  return SIDEBAR_DEFAULT_WIDTH;
}

interface SidebarProviderProps {
  children: ReactNode;
}

export function SidebarProvider({ children }: SidebarProviderProps) {
  const [isCollapsed, setIsCollapsedState] = useState(getInitialCollapsed);
  const [sidebarWidth, setSidebarWidthState] = useState(getInitialWidth);
  const [isMobileOpen, setIsMobileOpenState] = useState(false);

  const setIsCollapsed = useCallback((collapsed: boolean) => {
    setIsCollapsedState(collapsed);
    writeStoredValue(COLLAPSED_STORAGE_KEY, String(collapsed));
  }, []);

  const toggleCollapsed = useCallback(() => {
    setIsCollapsed(!isCollapsed);
  }, [isCollapsed, setIsCollapsed]);

  const setSidebarWidth = useCallback((width: number) => {
    setSidebarWidthState(width);
    writeStoredValue(WIDTH_STORAGE_KEY, String(width));
  }, []);

  const value: SidebarContextValue = useMemo(() => ({
    isCollapsed,
    setIsCollapsed,
    toggleCollapsed,
    sidebarWidth,
    setSidebarWidth,
    isMobileOpen,
    setIsMobileOpen: setIsMobileOpenState,
  }), [
    isCollapsed,
    setIsCollapsed,
    toggleCollapsed,
    sidebarWidth,
    setSidebarWidth,
    isMobileOpen,
  ]);

  return (
    <SidebarContext.Provider value={value}>
      {children}
    </SidebarContext.Provider>
  );
}

export function useSidebar(): SidebarContextValue {
  const context = useContext(SidebarContext);
  if (!context) {
    throw new Error('useSidebar must be used within a SidebarProvider');
  }
  return context;
}
