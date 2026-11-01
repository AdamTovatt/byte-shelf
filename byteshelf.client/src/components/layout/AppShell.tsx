import type { ReactNode } from 'react';
import { IconButton, iconMap } from '../../ui';
import { SideNav } from './SideNav';
import { MobileNavDrawer } from './MobileNavDrawer';
import { SidebarProvider, useSidebar } from '../../contexts/SidebarContext';

interface AppShellProps {
  children: ReactNode;
}

function AppShellContent({ children }: AppShellProps) {
  const { setIsMobileOpen } = useSidebar();

  return (
    <div className="h-screen flex bg-canvas">
      <div className="hidden md:block">
        <SideNav />
      </div>

      <MobileNavDrawer />

      <div className="flex-1 min-w-0 flex flex-col">
        <header className="md:hidden h-14 flex items-center justify-between px-4 border-b border-border bg-panel shrink-0">
          <IconButton
            variant="ghost"
            icon={iconMap.menu}
            onClick={() => setIsMobileOpen(true)}
            aria-label="Open navigation menu"
          />
          <span className="text-heading-sm text-fg">ByteShelf</span>
          <div className="w-9" />
        </header>

        <main className="flex-1 min-h-0 overflow-y-auto">
          {children}
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
