// Semantic mapping of icon names to imported SVG components.
//
// New icons: save the SVG to src/assets/icons/, add an export to src/assets/icons/index.ts,
// import it here and add an entry under the appropriate section.
//
// Call sites use `iconMap.<name>` so swapping or renaming icons happens in one place.

import {
  Add01Icon,
  ChevronLeft,
  ChevronRight,
  ComputerIcon,
  Delete02Icon,
  DownloadIcon,
  Edit04Icon,
  FileIcon,
  FolderIcon,
  Home11Icon,
  KeyIcon,
  Logout02Icon,
  Menu01Icon,
  Moon02Icon,
  MoreHorizontalIcon,
  PanelLeftIcon,
  RestoreIcon,
  Settings01Icon,
  ShelfIcon,
  Sun03Icon,
  UploadIcon,
  UserGroupIcon,
  ViewIcon,
  ViewOffSlashIcon,
} from '../assets/icons';

export const iconMap = {
  // Navigation chrome
  sidebar: PanelLeftIcon,
  menu: Menu01Icon,
  chevronLeft: ChevronLeft,
  chevronRight: ChevronRight,

  // Pages
  home: Home11Icon,
  settings: Settings01Icon,
  users: UserGroupIcon,

  // Theme
  themeSystem: ComputerIcon,
  themeLight: Sun03Icon,
  themeDark: Moon02Icon,

  // Account
  logout: Logout02Icon,

  // Shelves
  shelf: ShelfIcon,
  folder: FolderIcon,
  file: FileIcon,
  upload: UploadIcon,
  download: DownloadIcon,
  restore: RestoreIcon,
  key: KeyIcon,

  // Common actions
  edit: Edit04Icon,
  add: Add01Icon,
  delete: Delete02Icon,
  more: MoreHorizontalIcon,
  view: ViewIcon,
  viewOff: ViewOffSlashIcon,
} as const;

export type IconMapKey = keyof typeof iconMap;
