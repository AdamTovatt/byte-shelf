import { resolve } from 'node:path';
import ts from 'typescript';
import { beforeAll, expect, it } from 'vitest';

let checker: ts.TypeChecker;
let exported: ts.Symbol[];

// Reading the module's types takes a while on a busy machine.
beforeAll(() => {
  const entry = resolve('src/plugin/index.ts');
  const { config } = ts.readConfigFile(resolve('tsconfig.app.json'), ts.sys.readFile) as { config: { compilerOptions: object } };
  const { options } = ts.convertCompilerOptionsFromJson(config.compilerOptions, resolve('.'));
  const program = ts.createProgram([entry], { ...options, tsBuildInfoFile: undefined });
  checker = program.getTypeChecker();
  exported = checker.getExportsOfModule(checker.getSymbolAtLocation(program.getSourceFile(entry)!)!);
}, 30_000);

/** The properties of the type plugins import as `type`: of a record, generated from the server's, so a change there changes what plugins are given. */
function propertiesOf(type: string): string[] {
  const declared = checker.getDeclaredTypeOfSymbol(checker.getAliasedSymbol(exported.find(({ name }) => name === type)!));
  return declared.getProperties()
    .map((property) => `${property.name}${property.flags & ts.SymbolFlags.Optional ? '?' : ''}: ${checker.typeToString(checker.getTypeOfSymbol(property))}`)
    .sort();
}

it('promises plugins this of a folder and of what is below it, and changing what a release holds of it, but for more, raises PluginApi.Version', () => {
  expect(propertiesOf('ShelfDirectory')).toMatchInlineSnapshot(`
    [
      "createdAt: string",
      "encrypted: boolean",
      "id: string",
      "name: string",
      "parentId?: string | null | undefined",
      "path: string",
      "shelfId: string",
    ]
  `);
  // Its folders and files are the records pinned here.
  expect(propertiesOf('DirectoryTree').map((property) => property.split(':')[0])).toEqual(['directories', 'directory', 'files', 'nextCursor?']);
});

it('promises plugins this of a file, and changing what a release holds of it, but for more, raises PluginApi.Version', () => {
  expect(propertiesOf('ShelfFile')).toMatchInlineSnapshot(`
    [
      "contentType: string",
      "createdAt: string",
      "derived?: { thumbnail?: string | undefined; preview?: string | undefined; full?: string | undefined; agent?: string | undefined; stream?: string | undefined; } | null | undefined",
      "description?: string | null | undefined",
      "directoryId: string",
      "encrypted: boolean",
      "id: string",
      "name: string",
      "path: string",
      "pluginFields?: { [key: string]: { [key: string]: { readonly text?: string | null | undefined; readonly number?: number | null | undefined; readonly date?: string | null | undefined; readonly flag?: boolean | null | undefined; }; }; } | null | undefined",
      "sha256: string",
      "shelfId: string",
      "sizeBytes: number",
      "updatedAt: string",
    ]
  `);
});

/** The members of an interface plugins import as `type`, as it is written, records by their names. */
function membersOf(type: string): string[] {
  const declared = checker.getDeclaredTypeOfSymbol(checker.getAliasedSymbol(exported.find(({ name }) => name === type)!));
  return declared.getProperties().map(({ name, valueDeclaration }) => {
    const { questionToken, type: written } = valueDeclaration as ts.PropertySignature;
    return `${type}.${name}${questionToken ? '?' : ''}: ${written!.getText()}`;
  });
}

it('promises plugins this of viewers, and changing what a release holds of it, but for more, raises PluginApi.Version', () => {
  expect(['Viewer', 'ViewerProps', 'Neighbour', 'DerivedReader', 'ContentReader'].flatMap(membersOf)).toMatchInlineSnapshot(`
    [
      "Viewer.id: string",
      "Viewer.mediaTypes: readonly string[]",
      "Viewer.extensions: readonly string[]",
      "Viewer.component: ComponentType<ViewerProps>",
      "Viewer.unavailable?: () => string | null",
      "Viewer.clearOfBars?: boolean",
      "ViewerProps.file: ShelfFile",
      "ViewerProps.content: ContentReader",
      "ViewerProps.derived: DerivedReader",
      "ViewerProps.neighbours: readonly Neighbour[]",
      "ViewerProps.Fallback: ComponentType",
      "Neighbour.file: ShelfFile",
      "Neighbour.derived: DerivedReader",
      "DerivedReader.has: (kind: DerivedKind) => boolean",
      "DerivedReader.url: (kind: DerivedKind, signal?: AbortSignal) => Promise<string | null>",
      "DerivedReader.forget: (kind: DerivedKind) => void",
      "DerivedReader.status: (kind: DerivedKind, signal?: AbortSignal) => Promise<DerivedStatus>",
      "ContentReader.url: (signal?: AbortSignal) => Promise<string>",
      "ContentReader.blob: (signal?: AbortSignal) => Promise<Blob>",
      "ContentReader.range: (start: number, end: number, signal?: AbortSignal) => Promise<Blob>",
    ]
  `);
});

it('promises plugins this of folder types, and changing what a release holds of it, but for more, raises PluginApi.Version', () => {
  expect(['FolderType', 'FolderViewerProps', 'FolderContents', 'LockableReader'].flatMap(membersOf)).toMatchInlineSnapshot(`
    [
      "FolderType.suffix: string",
      "FolderType.label: string",
      "FolderType.icon: IconComponent",
      "FolderType.component: ComponentType<FolderViewerProps>",
      "FolderViewerProps.shelf: string",
      "FolderViewerProps.folder: ShelfDirectory",
      "FolderViewerProps.contents: FolderContents",
      "FolderViewerProps.canWrite: boolean",
      "FolderContents.queryKey: readonly unknown[]",
      "FolderContents.list: (after?: string, signal?: AbortSignal) => Promise<DirectoryTree>",
      "FolderContents.read: (file: ShelfFile) => LockableReader",
      "FolderContents.store: (path: string, contents: Blob, signal?: AbortSignal) => Promise<ShelfFile>",
      "FolderContents.delete: (file: ShelfFile) => Promise<void>",
      "FolderContents.move: (file: ShelfFile, path: string) => Promise<ShelfFile>",
      "FolderContents.open: (file: ShelfFile) => void",
      "LockableReader.unlock: (signal?: AbortSignal) => Promise<boolean>",
      "LockableReader.close: () => void",
      "LockableReader.url: (signal?: AbortSignal) => Promise<string>",
      "LockableReader.blob: (signal?: AbortSignal) => Promise<Blob>",
      "LockableReader.range: (start: number, end: number, signal?: AbortSignal) => Promise<Blob>",
    ]
  `);
});

it('promises plugins these, and taking away one a release holds raises PluginApi.Version', () => {
  expect(exported.map(({ name }) => name).sort()).toMatchInlineSnapshot(`
    [
      "ActionRow",
      "ActionRowProps",
      "ApiError",
      "Avatar",
      "AvatarProps",
      "BarButton",
      "BarTextButton",
      "BreadcrumbItem",
      "Breadcrumbs",
      "BreadcrumbsProps",
      "Button",
      "ButtonGroup",
      "ButtonGroupOption",
      "ButtonGroupProps",
      "ButtonProps",
      "Card",
      "CardProps",
      "ContentReader",
      "CopyField",
      "CopyFieldProps",
      "CustomScroll",
      "CustomScrollProps",
      "DRAGS_ITSELF",
      "DerivedKind",
      "DerivedReader",
      "DerivedStatus",
      "DirectoryTree",
      "Divider",
      "DividerProps",
      "Dropdown",
      "DropdownItem",
      "DropdownItemProps",
      "DropdownOptionRow",
      "DropdownOptionRowProps",
      "DropdownProps",
      "EmptyState",
      "EmptyStateProps",
      "ErrorBoundary",
      "ErrorText",
      "ErrorTextProps",
      "FieldLabel",
      "FieldLabelProps",
      "FileAction",
      "FileActionContext",
      "FolderContents",
      "FolderType",
      "FolderViewerProps",
      "FormError",
      "FormErrorProps",
      "FullScreen",
      "Icon",
      "IconButton",
      "IconButtonProps",
      "IconComponent",
      "IconMapKey",
      "IconProps",
      "Input",
      "InputProps",
      "InvalidationResource",
      "ListButton",
      "ListButtonProps",
      "Loaded",
      "LoadedContent",
      "LockableReader",
      "LockedError",
      "Modal",
      "ModalProps",
      "Neighbour",
      "PAGE_CONTENT_WIDTH",
      "PageLayout",
      "PageLayoutProps",
      "PlainText",
      "PluginDefinition",
      "PluginDocumentWrite",
      "PluginDocuments",
      "Popover",
      "PopoverProps",
      "PreviewControls",
      "PreviewKeyHandler",
      "ProgressBar",
      "ProgressBarProps",
      "ReadText",
      "Reloadable",
      "ResolvedTheme",
      "RouteTab",
      "RouteTabProps",
      "SIDEBAR_COLLAPSED_WIDTH",
      "SIDEBAR_DEFAULT_WIDTH",
      "SIDEBAR_MAX_WIDTH",
      "SIDEBAR_MIN_WIDTH",
      "Select",
      "SelectOption",
      "SelectProps",
      "ShelfDirectory",
      "ShelfFile",
      "SideNavItem",
      "SideNavItemProps",
      "Sidebar",
      "SidebarFooter",
      "SidebarHeader",
      "SidebarSection",
      "Skeleton",
      "SkeletonProps",
      "Spinner",
      "SpinnerProps",
      "Tab",
      "TabGroup",
      "TabGroupProps",
      "TabProps",
      "TextPane",
      "Textarea",
      "TextareaProps",
      "Theme",
      "ThemeProvider",
      "Tooltip",
      "TooltipProps",
      "Viewer",
      "ViewerProps",
      "Zoom",
      "authorizedFetch",
      "clamp",
      "cn",
      "colors",
      "definePlugin",
      "formatDuration",
      "getErrorMessage",
      "getFileIcon",
      "iconMap",
      "invalidateResource",
      "loadDerived",
      "loadImage",
      "mediaTypeOf",
      "readStoredValue",
      "readText",
      "relativeTo",
      "throwIfFailed",
      "toast",
      "useCopyFeedback",
      "useLoaded",
      "usePluginDocuments",
      "usePreviewFullScreen",
      "usePreviewKeys",
      "useSetPluginDocument",
      "useSidebarState",
      "useTheme",
      "useZoom",
      "writeStoredValue",
    ]
  `);
});
