// Semantic mapping of icon names to SVG components. Call sites use `iconMap.<name>`, so swapping an
// icon happens here. A new icon: save its SVG in ../assets/icons, export it from that folder's index,
// and add an entry below.

import { IMAGE_EXTENSIONS, PDF_EXTENSIONS, TEXT_DOCUMENT_EXTENSIONS, isWordDocumentFileName } from './fileExtensions';
import {
  Alert01Icon,
  Cancel01Icon,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  ComputerIcon,
  Copy01Icon,
  Csv02Icon,
  Delete01Icon,
  Doc02Icon,
  Download01Icon,
  Edit04Icon,
  File02Icon,
  FileChartColumnIcon,
  FileImageIcon,
  FileQuestionMarkIcon,
  FileZipIcon,
  Folder01Icon,
  Folder02Icon,
  Home11Icon,
  InformationCircleIcon,
  KeyIcon,
  Logout02Icon,
  Menu01Icon,
  Moon02Icon,
  MoreHorizontalIcon,
  MoveRightIcon,
  PanelLeftIcon,
  Pdf02Icon,
  PlusSignIcon,
  ReloadIcon,
  RestoreIcon,
  Settings01Icon,
  ShelfIcon,
  Shield01Icon,
  SquareArrowUpRightIcon,
  Sun03Icon,
  TextSquareIcon,
  Tick01Icon,
  Undo03Icon,
  Upload01Icon,
  UserMultiple02Icon,
  ViewIcon,
  ViewOffSlashIcon,
} from '../assets/icons';

export const iconMap = {
  // Navigation
  sidebar: PanelLeftIcon,
  menu: Menu01Icon,
  home: Home11Icon,
  settings: Settings01Icon,
  users: UserMultiple02Icon,
  admin: Shield01Icon,
  replication: ReloadIcon,
  chevronDown: ChevronDown,
  chevronUp: ChevronUp,
  chevronLeft: ChevronLeft,
  chevronRight: ChevronRight,

  // Theme
  themeSystem: ComputerIcon,
  themeLight: Sun03Icon,
  themeDark: Moon02Icon,

  // Account
  logout: Logout02Icon,

  // Shelves and their contents
  shelf: ShelfIcon,
  folder: Folder01Icon,
  projectOpen: Folder02Icon,
  file: File02Icon,
  fileCsv: Csv02Icon,
  fileDoc: Doc02Icon,
  filePdf: Pdf02Icon,
  fileSpreadsheet: FileChartColumnIcon,
  fileImage: FileImageIcon,
  fileUnknown: FileQuestionMarkIcon,
  fileZip: FileZipIcon,
  upload: Upload01Icon,
  download: Download01Icon,
  restore: RestoreIcon,
  key: KeyIcon,

  // Actions
  add: PlusSignIcon,
  edit: Edit04Icon,
  rename: TextSquareIcon,
  move: MoveRightIcon,
  delete: Delete01Icon,
  undo: Undo03Icon,
  copy: Copy01Icon,
  close: Cancel01Icon,
  more: MoreHorizontalIcon,
  view: ViewIcon,
  viewOff: ViewOffSlashIcon,
  externalLink: SquareArrowUpRightIcon,

  // Status
  success: Tick01Icon,
  alert: Alert01Icon,
  info: InformationCircleIcon,
} as const;

export type IconMapKey = keyof typeof iconMap;

/** The icon for a file, by its name's extension. */
export function getFileIcon(fileName: string): typeof iconMap.file {
  const lower = fileName.toLowerCase();

  if (lower.endsWith('.csv') || lower.endsWith('.tsv') || lower.endsWith('.csv.gz') || lower.endsWith('.tsv.gz'))
    return iconMap.fileCsv;
  if (lower.endsWith('.xlsx') || lower.endsWith('.db') || lower.endsWith('.sqlite') || lower.endsWith('.sqlite3'))
    return iconMap.fileSpreadsheet;
  if (IMAGE_EXTENSIONS.some((ext) => lower.endsWith(ext))) return iconMap.fileImage;
  if (isWordDocumentFileName(lower)) return iconMap.fileDoc;
  if (PDF_EXTENSIONS.some((ext) => lower.endsWith(ext))) return iconMap.filePdf;
  if (TEXT_DOCUMENT_EXTENSIONS.some((ext) => lower.endsWith(ext))) return iconMap.file;
  if (['.zip', '.gz', '.tar', '.rar', '.7z'].some((ext) => lower.endsWith(ext))) return iconMap.fileZip;
  return iconMap.fileUnknown;
}
