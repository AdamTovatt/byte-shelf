import { useEffect, useRef } from 'react';
import { useLocation } from 'react-router-dom';
import { useSidebar } from '../../contexts/SidebarContext';
import { SideNav } from './SideNav';
import { cn } from '../../ui';

export function MobileNavDrawer() {
  const { isMobileOpen, setIsMobileOpen } = useSidebar();
  const location = useLocation();
  const drawerRef = useRef<HTMLDivElement>(null);
  const prevPathnameRef = useRef(location.pathname);

  useEffect(() => {
    if (prevPathnameRef.current !== location.pathname && isMobileOpen) {
      setIsMobileOpen(false);
    }
    prevPathnameRef.current = location.pathname;
  }, [location.pathname, isMobileOpen, setIsMobileOpen]);

  useEffect(() => {
    if (!isMobileOpen) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setIsMobileOpen(false);
      }
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [isMobileOpen, setIsMobileOpen]);

  useEffect(() => {
    if (isMobileOpen) {
      document.body.style.overflow = 'hidden';
    } else {
      document.body.style.overflow = '';
    }
    return () => {
      document.body.style.overflow = '';
    };
  }, [isMobileOpen]);

  if (!isMobileOpen) return null;

  return (
    <div className="fixed inset-0 z-50 md:hidden">
      <div
        className="absolute inset-0 bg-black/50 backdrop-blur-sm animate-fade-in"
        onClick={() => setIsMobileOpen(false)}
        aria-hidden="true"
      />

      <div
        ref={drawerRef}
        className={cn(
          'absolute left-0 top-0 bottom-0 w-72 max-w-[85vw] animate-sidebar-slide-in',
        )}
      >
        <SideNav />
      </div>
    </div>
  );
}
