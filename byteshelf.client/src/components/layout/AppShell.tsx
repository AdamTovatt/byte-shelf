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
          {/* Three columns, the outer two of equal width, so the title stays centred whatever a page
              puts at the right end (MobileTopBarActions). */}
          <header ref={setBar} className="md:hidden relative h-14 grid grid-cols-[1fr_auto_1fr] items-center px-4 border-b border-border bg-panel shrink-0">
            <div>
              <IconButton
                variant="ghost"
                icon={iconMap.menu}
                onClick={() => setIsMobileOpen(true)}
                aria-label="Open navigation menu"
              />
            </div>
            <span className="text-heading-sm text-fg">ByteShelf</span>
            <div ref={setActions} className="flex items-center justify-end gap-1" />
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
