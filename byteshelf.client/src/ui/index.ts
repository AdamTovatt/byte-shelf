// The UI library: buttons, fields, cards and rows, dialogs, menus, page layout and the sidebar, with
// the colour, type and icon systems they are drawn with. Pages import from here.

export { ActionRow, type ActionRowProps } from './components/ui/ActionRow';
export { Avatar, type AvatarProps } from './components/ui/Avatar';
export { Breadcrumbs, type BreadcrumbItem, type BreadcrumbsProps } from './components/ui/Breadcrumbs';
export { Button, type ButtonProps } from './components/ui/Button';
export { ButtonGroup, type ButtonGroupProps, type ButtonGroupOption } from './components/ui/ButtonGroup';
export { Card, type CardProps } from './components/ui/Card';
export { CopyField, type CopyFieldProps } from './components/ui/CopyField';
export { Divider, type DividerProps } from './components/ui/Divider';
export { Dropdown, DropdownItem, type DropdownProps, type DropdownItemProps } from './components/ui/Dropdown';
export { EmptyState, type EmptyStateProps } from './components/ui/EmptyState';
export { ErrorText, type ErrorTextProps } from './components/ui/ErrorText';
export { FieldLabel, type FieldLabelProps } from './components/ui/FieldLabel';
export { FormError, type FormErrorProps } from './components/ui/FormError';
export { Icon, type IconComponent, type IconProps } from './components/ui/Icon';
export { IconButton, type IconButtonProps } from './components/ui/IconButton';
export { Input, type InputProps } from './components/ui/Input';
export { ListButton, type ListButtonProps } from './components/ui/ListButton';
export { Modal, type ModalProps } from './components/ui/Modal';
export { PageLayout, type PageLayoutProps } from './components/ui/PageLayout';
export { PAGE_CONTENT_WIDTH } from './components/ui/pageWidth';
export { Popover, type PopoverProps } from './components/ui/Popover';
export { ProgressBar, type ProgressBarProps } from './components/ui/ProgressBar';
export { Select, type SelectProps, type SelectOption } from './components/ui/Select';
export {
  Sidebar,
  SidebarHeader,
  SidebarSection,
  SidebarFooter,
  useSidebarState,
  SIDEBAR_MIN_WIDTH,
  SIDEBAR_MAX_WIDTH,
  SIDEBAR_COLLAPSED_WIDTH,
  SIDEBAR_DEFAULT_WIDTH,
} from './components/ui/Sidebar';
export { SideNavItem, type SideNavItemProps } from './components/ui/SideNavItem';
export { Skeleton, type SkeletonProps } from './components/ui/Skeleton';
export { Spinner, type SpinnerProps } from './components/ui/Spinner';
export { RouteTab, Tab, TabGroup, type RouteTabProps, type TabGroupProps, type TabProps } from './components/ui/TabBar';
export { Textarea, type TextareaProps } from './components/ui/Textarea';
export { Tooltip, type TooltipProps } from './components/ui/Tooltip';

export { ThemeProvider, useTheme, type Theme, type ResolvedTheme } from './contexts/ThemeContext';
export { useCopyFeedback } from './hooks/useCopyFeedback';
export { iconMap, getFileIcon, type IconMapKey } from './lib/iconMap';
export { cn, clamp, getErrorMessage } from './lib/utils';
export { colors } from './lib/colors';
