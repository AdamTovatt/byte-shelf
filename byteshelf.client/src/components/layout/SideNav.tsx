import { useLocation, useNavigate } from 'react-router-dom';
import { useShelves } from '../../hooks/useShelfQueries';
import { useIsAdmin } from '../../hooks/useUserQueries';
import { shelfRoute } from '../../lib/shelfPaths';
import { iconMap } from '../../lib/iconMap';
import { Icon } from '../ui/Icon';
import { IconButton } from '../ui/IconButton';
import { SideNavItem, Avatar, Button, ButtonGroup, Dropdown, DropdownItem, Tooltip, Logo } from '../ui';
import { useTheme } from '../../contexts/ThemeContext';
import { Sidebar, SidebarHeader, SidebarSection, SidebarFooter } from '../ui/Sidebar';
import { useSidebar, SIDEBAR_MIN_WIDTH, SIDEBAR_MAX_WIDTH, SIDEBAR_COLLAPSED_WIDTH } from '../../contexts/SidebarContext';
import { useAuth } from '../../contexts/AuthContext';
import { useIsActive } from '../../hooks/useIsActive';
import { cn } from '../../lib/utils';

export function SideNav() {
  const navigate = useNavigate();
  const location = useLocation();
  const { logout } = useAuth();
  const { data: shelves } = useShelves();
  const isAdmin = useIsAdmin();
  const { theme, setTheme } = useTheme();
  const isActive = useIsActive();
  const {
    isCollapsed,
    setIsCollapsed,
    toggleCollapsed,
    sidebarWidth,
    setSidebarWidth,
  } = useSidebar();

  return (
    <Sidebar
      role="navigation"
      collapsed={isCollapsed}
      onCollapsedChange={setIsCollapsed}
      width={sidebarWidth}
      onWidthChange={setSidebarWidth}
      showCollapseToggle={false}
      autoCollapseOnDrag
      forceDark
      minWidth={SIDEBAR_MIN_WIDTH}
      maxWidth={SIDEBAR_MAX_WIDTH}
      collapsedWidth={SIDEBAR_COLLAPSED_WIDTH}
      className="h-screen"
    >
      <SidebarHeader>
        <div
          className={cn(
            'h-16 flex items-center',
            isCollapsed ? 'justify-center px-2' : 'justify-between px-4',
          )}
        >
          {!isCollapsed && (
            <Button
              variant="text"
              onClick={() => navigate('/')}
              className="!gap-2 text-heading-sm text-text-1-light dark:text-text-1-dark hover:opacity-80 transition-opacity"
              icon={<Logo className="h-6 w-6 rounded-md" />}
            >
              ByteShelf
            </Button>
          )}
          <Tooltip content={isCollapsed ? 'Open sidebar' : 'Close sidebar'} position="right">
            <IconButton
              variant="ghost"
              size="md"
              icon={iconMap.sidebar}
              iconStrokeWidth={2}
              onClick={toggleCollapsed}
              aria-label={isCollapsed ? 'Open sidebar' : 'Close sidebar'}
            />
          </Tooltip>
        </div>
      </SidebarHeader>

      <SidebarSection scrollable padding="sm" className="space-y-1">
        <SideNavItem
          label="Shelves"
          icon={iconMap.shelf}
          href="/"
          isActive={location.pathname === '/'}
          isCollapsed={isCollapsed}
          depth={0}
        />

        {!isCollapsed && shelves?.map((shelf) => (
          <SideNavItem
            key={shelf.id}
            label={shelf.name}
            href={shelfRoute(shelf.name)}
            isActive={location.pathname === shelfRoute(shelf.name) || location.pathname.startsWith(`${shelfRoute(shelf.name)}/`)}
            isCollapsed={isCollapsed}
            depth={1}
          />
        ))}

        {isAdmin && (
          <SideNavItem
            label="Users"
            icon={iconMap.users}
            href="/users"
            isActive={isActive('/users')}
            isCollapsed={isCollapsed}
            depth={0}
          />
        )}

        <SideNavItem
          label="Settings"
          icon={iconMap.settings}
          href="/settings"
          isActive={isActive('/settings')}
          isCollapsed={isCollapsed}
          depth={0}
        />
      </SidebarSection>

      <SidebarFooter className="px-2 pb-2 pt-2">
        <Dropdown
          trigger={
            <SideNavItem
              label="Account"
              iconElement={<Avatar size="md" />}
              isCollapsed={isCollapsed}
              depth={0}
            />
          }
          position="above"
          className="w-full"
        >
          <div className="px-2 py-1.5">
            <ButtonGroup
              options={[
                { value: 'system', icon: <Icon icon={iconMap.themeSystem} size={14} />, tooltip: 'System' },
                { value: 'light', icon: <Icon icon={iconMap.themeLight} size={14} />, tooltip: 'Light' },
                { value: 'dark', icon: <Icon icon={iconMap.themeDark} size={14} />, tooltip: 'Dark' },
              ]}
              value={theme}
              onChange={setTheme}
              size="sm"
              className="w-full"
            />
          </div>
          <DropdownItem
            icon={<Icon icon={iconMap.logout} size={16} />}
            variant="default"
            onClick={() => void logout()}
          >
            Log out
          </DropdownItem>
        </Dropdown>
      </SidebarFooter>
    </Sidebar>
  );
}
