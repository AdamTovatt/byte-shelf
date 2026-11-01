import { useLocation, useNavigate } from 'react-router-dom';
import { useShelves } from '../../hooks/useShelfQueries';
import { useCurrentUser, useIsAdmin } from '../../hooks/useUserQueries';
import { shelfRoute } from '../../lib/shelfPaths';
import { ROUTES } from '../../lib/routes';
import {
  Avatar,
  Button,
  ButtonGroup,
  Dropdown,
  DropdownItem,
  Icon,
  IconButton,
  Sidebar,
  SidebarFooter,
  SidebarHeader,
  SidebarSection,
  SideNavItem,
  SIDEBAR_COLLAPSED_WIDTH,
  SIDEBAR_MAX_WIDTH,
  SIDEBAR_MIN_WIDTH,
  Tooltip,
  cn,
  iconMap,
  useTheme,
  type ButtonGroupOption,
  type Theme,
} from '../../ui';
import { Logo } from '../Logo';
import { useSidebar } from '../../contexts/SidebarContext';
import { useAuth } from '../../contexts/AuthContext';

const THEME_OPTIONS: ButtonGroupOption<Theme>[] = [
  { value: 'system', icon: <Icon icon={iconMap.themeSystem} size={14} />, tooltip: 'System' },
  { value: 'light', icon: <Icon icon={iconMap.themeLight} size={14} />, tooltip: 'Light' },
  { value: 'dark', icon: <Icon icon={iconMap.themeDark} size={14} />, tooltip: 'Dark' },
];

/** Whether `pathname` is the page at `path` or one under it. */
function isWithin(pathname: string, path: string): boolean {
  return pathname === path || pathname.startsWith(`${path}/`);
}

export function SideNav() {
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const { logout } = useAuth();
  const { data: shelves } = useShelves();
  const { data: me } = useCurrentUser();
  const isAdmin = useIsAdmin();
  const { theme, setTheme } = useTheme();
  const { isCollapsed, setIsCollapsed, toggleCollapsed, sidebarWidth, setSidebarWidth } = useSidebar();

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
        <div className={cn('h-16 flex items-center', isCollapsed ? 'justify-center px-2' : 'justify-between px-4')}>
          {!isCollapsed && (
            <Button
              variant="text"
              onClick={() => navigate(ROUTES.HOME)}
              className="!gap-2 text-heading-sm text-fg hover:opacity-80 transition-opacity"
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
              onClick={toggleCollapsed}
              aria-label={isCollapsed ? 'Open sidebar' : 'Close sidebar'}
            />
          </Tooltip>
        </div>
      </SidebarHeader>

      <SidebarSection scrollable padding="sm" className="space-y-1">
        <SideNavItem label="Shelves" icon={iconMap.shelf} href={ROUTES.HOME} isActive={pathname === ROUTES.HOME} isCollapsed={isCollapsed} depth={0} />

        {!isCollapsed && shelves?.map((shelf) => (
          <SideNavItem
            key={shelf.id}
            label={shelf.name}
            href={shelfRoute(shelf.name)}
            isActive={isWithin(pathname, shelfRoute(shelf.name))}
            isCollapsed={isCollapsed}
            depth={1}
          />
        ))}

        {isAdmin && (
          <>
            <SideNavItem label="Users" icon={iconMap.users} href={ROUTES.USERS} isActive={isWithin(pathname, ROUTES.USERS)} isCollapsed={isCollapsed} depth={0} />
            <SideNavItem label="Replication" icon={iconMap.replication} href={ROUTES.REPLICATION} isActive={isWithin(pathname, ROUTES.REPLICATION)} isCollapsed={isCollapsed} depth={0} />
          </>
        )}

        <SideNavItem label="Settings" icon={iconMap.settings} href={ROUTES.SETTINGS} isActive={isWithin(pathname, ROUTES.SETTINGS)} isCollapsed={isCollapsed} depth={0} />
      </SidebarSection>

      <SidebarFooter className="px-2 pb-2 pt-2">
        <Dropdown
          trigger={
            <SideNavItem
              label={me?.displayName ?? 'Account'}
              iconElement={<Avatar size="sm" name={me?.displayName ?? me?.email} />}
              isCollapsed={isCollapsed}
              depth={0}
            />
          }
          position="above"
          className="w-full"
        >
          {me && <div className="px-3 pt-2 pb-1 text-body-sm text-muted truncate">{me.email}</div>}
          <div className="px-2 py-1.5">
            <ButtonGroup options={THEME_OPTIONS} value={theme} onChange={setTheme} size="sm" ariaLabel="Theme" className="w-full" />
          </div>
          <DropdownItem icon={<Icon icon={iconMap.logout} size={16} />} onClick={() => void logout()}>
            Log out
          </DropdownItem>
        </Dropdown>
      </SidebarFooter>
    </Sidebar>
  );
}
