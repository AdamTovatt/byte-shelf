import { useMemo, useState, type ReactNode } from 'react';
import { IconButton, iconMap } from '../../ui';
import { MobileTopBarContext } from '../../contexts/MobileTopBarContext';
import { SideNav } from './SideNav';
import { MobileNavDrawer } from './MobileNavDrawer';
import { SidebarProvider, useSidebar } from '../../contexts/SidebarContext';

interface AppShellProps {
  children: ReactNode;
}

function AppShellContent({ children }: AppShellProps) {
  const { setIsMobileOpen } = useSidebar();
  const [bar, setBar] = useState<HTMLElement | null>(null);
  const [actions, setActions] = useState<HTMLElement | null>(null);
  const slots = useMemo(() => ({ bar, actions }), [bar, actions]);

  return (
    <MobileTopBarContext.Provider value={slots}>
      <div className="h-screen flex bg-canvas">
        <div className="hidden md:block">
          <SideNav />
        </div>

        <MobileNavDrawer />

        <div className="flex-1 min-w-0 flex flex-col">
          {/* The menu button, then whatever the page puts in the rest of the bar (MobileTopBarActions). */}
          <header ref={setBar} className="md:hidden relative h-14 flex items-center gap-2 px-4 border-b border-border bg-panel shrink-0">
            <IconButton
              variant="ghost"
              icon={iconMap.menu}
              onClick={() => setIsMobileOpen(true)}
              aria-label="Open navigation menu"
            />
            <div ref={setActions} className="min-w-0 flex-1 flex items-center gap-2" />
          </header>

          <main className="flex-1 min-h-0 overflow-y-auto">
            {children}
          </main>
        </div>
      </div>
    </MobileTopBarContext.Provider>
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
