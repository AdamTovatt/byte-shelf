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

// Generated from the server's, so a change there changes what plugins are given.
it('promises plugins this of a file, and changing what a release holds of it, but for more, raises PluginApi.Version', () => {
  const file = checker.getDeclaredTypeOfSymbol(checker.getAliasedSymbol(exported.find(({ name }) => name === 'ShelfFile')!));
  const properties = file.getProperties().map((property) => `${property.name}${property.flags & ts.SymbolFlags.Optional ? '?' : ''}: ${checker.typeToString(checker.getTypeOfSymbol(property))}`);

  expect(properties.sort()).toMatchInlineSnapshot(`
    [
      "contentType: string",
      "createdAt: string",
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

it('promises plugins these, and taking away one a release holds raises PluginApi.Version', () => {
  expect(exported.map(({ name }) => name).sort()).toMatchInlineSnapshot(`
    [
      "ActionRow",
      "ActionRowProps",
      "ApiError",
      "Avatar",
      "AvatarProps",
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
      "FormError",
      "FormErrorProps",
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
      "Modal",
      "ModalProps",
      "PAGE_CONTENT_WIDTH",
      "PageLayout",
      "PageLayoutProps",
      "PlainText",
      "PluginDefinition",
      "PluginDocumentWrite",
      "PluginDocuments",
      "Popover",
      "PopoverProps",
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
      "authorizedFetch",
      "clamp",
      "cn",
      "colors",
      "definePlugin",
      "getErrorMessage",
      "getFileIcon",
      "iconMap",
      "invalidateResource",
      "readText",
      "throwIfFailed",
      "toast",
      "useCopyFeedback",
      "useLoaded",
      "usePluginDocuments",
      "useSetPluginDocument",
      "useSidebarState",
      "useTheme",
    ]
  `);
});
