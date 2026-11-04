import { type ReactNode, type ButtonHTMLAttributes, type Ref, useId } from 'react';
import { Icon, type IconComponent } from './Icon';
import { ICON_TONE_CLASS, carriesColorClass, type ListButtonIconTone } from './listButtonIconTone';
import { cn } from '../../lib/utils';
import { menuRowClass } from './menuPanelStyles';

export type ListButtonAppearance = 'plain' | 'tinted' | 'menu';
export type { ListButtonIconTone };

export interface ListButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  icon?: IconComponent;
  iconElement?: ReactNode;
  iconSize?: number;
  /**
   * Extra classes for the icon slot — spacing, transitions, opacity. **Never colour**: colour goes
   * through `iconTone`. `cn()` concatenates rather than merging, so a colour class here would land
   * on the same span as the tone class and stylesheet order (not argument order) would pick the
   * winner.
   */
  iconClassName?: string;
  /**
   * Colour of the icon slot, kept separate from `iconClassName` so a row icon can never end up
   * uncoloured — an uncoloured `stroke="currentColor"` icon inherits the document foreground, which
   * is louder than a row icon should be. `muted` (default) is the row-icon colour; `inherit` emits
   * no colour class, for rows that drive the icon colour from the row itself (`SideNavItem`) or
   * whose `iconElement` colours its own contents.
   */
  iconTone?: ListButtonIconTone;
  isActive?: boolean;
  /**
   * Built-in active/hover styling. `plain` (default) leaves visual state to the caller's
   * `className`. `tinted` paints a brand-tint background when `isActive` and a surface hover
   * otherwise — the standard "selectable card row" look used by template/file pickers. `menu` is the
   * shared menu row (`menuPanelStyles.ts`) for a row inside a menu-style popover: the inset pill,
   * `row-hover` under the pointer, and the same selected tint `DropdownOptionRow` draws when
   * `isActive` — so a picker built on this row and a `Select` beside it cannot disagree.
   */
  appearance?: ListButtonAppearance;
  actions?: ReactNode;
  children: ReactNode;
  /**
   * The row's click target — the overlay button, not the row. For a caller that has to put focus
   * back on the row, e.g. after a control inside it removes itself.
   */
  ref?: Ref<HTMLButtonElement>;
}

const TINTED_ACTIVE = 'bg-brand-tint/10';
const TINTED_INACTIVE = 'hover:bg-surface';

export function ListButton({
  icon,
  iconElement,
  iconSize = 16,
  iconClassName,
  iconTone = 'muted',
  isActive = false,
  appearance = 'plain',
  actions,
  children,
  className,
  type = 'button',
  ...props
}: ListButtonProps) {
  const appearanceClass =
    appearance === 'tinted'
      ? (isActive ? TINTED_ACTIVE : TINTED_INACTIVE)
      : appearance === 'menu'
        ? menuRowClass({ selected: isActive, canAct: !props.disabled })
        : undefined;
  const labelId = useId();

  const toneClass = ICON_TONE_CLASS[iconTone];

  // Surfaced in dev rather than left to review (the collision it prevents is described on
  // `iconClassName` above). Gated on a tone class actually being emitted AND an icon actually being
  // rendered: under `inherit`, or with no icon, there is nothing to collide with, so a colour passed
  // there is unambiguous and warning would be a false alarm.
  if (
    import.meta.env.DEV &&
    (icon || iconElement) &&
    toneClass &&
    iconClassName &&
    carriesColorClass(iconClassName)
  ) {
    console.warn(
      `ListButton: iconClassName "${iconClassName}" carries a colour class. Colour must go through ` +
        'the iconTone prop (muted | inherit); iconClassName is for spacing, transitions ' +
        'and opacity only.',
    );
  }

  // The row is a plain container carrying the caller's `className` (padding, hover, rounded, gap,
  // justify, text). The clickable area is a transparent <button> stretched over the whole row
  // (`absolute inset-0`) sitting BEHIND the content (`-z-10`, scoped by the row's `isolate`). The
  // icon/label render in normal flow as `pointer-events-none` siblings, so a click anywhere in the
  // padded/rounded row — including the band above/below the text — falls through to the button and
  // fires `onClick`. `actions` render as siblings too: this keeps interactive controls (delete,
  // dropdowns, checkboxes, toggles) out of the <button> (nesting them is invalid HTML), and, being
  // painted above the `-z-10` overlay, they still receive their own clicks. Dropdown menus are
  // unaffected by `isolate` because `Popover` portals them to <body>. The button derives its
  // accessible name from the label via `aria-labelledby` (callers that render no `children`, e.g. a
  // collapsed icon-only row, pass their own `aria-label`).
  const cursorClass = props.disabled
    ? 'cursor-not-allowed'
    : props.onClick
      ? 'cursor-pointer'
      : 'cursor-default';
  const hasOwnLabel = props['aria-label'] != null || props['aria-labelledby'] != null;

  return (
    <div
      data-active={isActive || undefined}
      className={cn(
        'relative isolate w-full flex items-center transition-colors select-none group',
        appearanceClass,
        className,
      )}
    >
      <button
        type={type}
        className={cn('absolute inset-0 -z-10 rounded-[inherit]', cursorClass)}
        aria-labelledby={children != null && !hasOwnLabel ? labelId : undefined}
        {...props}
      />

      {(icon || iconElement) && (
        <span
          className={cn(
            'shrink-0 flex items-center pointer-events-none',
            toneClass,
            iconClassName,
          )}
        >
          {iconElement ?? <Icon icon={icon!} size={iconSize} />}
        </span>
      )}

      {children != null && (
        <span id={labelId} className="min-w-0 flex-1 pointer-events-none">
          {children}
        </span>
      )}

      {actions}
    </div>
  );
}
