import type { ReactNode } from 'react';
import { iconMap } from '../../lib/iconMap';
import { IconButton } from '../ui/IconButton';
import { SideNav } from './SideNav';
import { MobileNavDrawer } from './MobileNavDrawer';
import { SidebarProvider, useSidebar } from '../../contexts/SidebarContext';

interface AppShellProps {
  children: ReactNode;
}

function AppShellContent({ children }: AppShellProps) {
  const { setIsMobileOpen } = useSidebar();

  return (
    <div className="h-screen flex bg-elevation-1-light dark:bg-elevation-1-dark">
      <div className="hidden md:block">
        <SideNav />
      </div>

      <MobileNavDrawer />

      <div className="flex-1 min-w-0 flex flex-col">
        <header className="md:hidden h-14 flex items-center justify-between px-4 border-b border-border-light dark:border-border-dark bg-elevation-2-light dark:bg-elevation-2-dark shrink-0">
          <IconButton
            variant="ghost"
            icon={iconMap.menu}
            onClick={() => setIsMobileOpen(true)}
            aria-label="Open navigation menu"
          />
          <span className="text-heading-sm text-text-1-light dark:text-text-1-dark">ByteShelf</span>
          <div className="w-9" />
        </header>

        <main className="flex-1 min-h-0 flex flex-col overflow-y-auto">
          <div className="w-full max-w-[80rem] mx-auto flex-1 min-h-0 flex flex-col">
            {children}
          </div>
        </main>
      </div>
    </div>
  );
}

export function AppShell({ children }: AppShellProps) {
  return (
    <SidebarProvider>
      <AppShellContent>
        {children}
      </AppShellContent>
    </SidebarProvider>
  );
}
