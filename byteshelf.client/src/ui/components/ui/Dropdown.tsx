import { type ReactNode, type RefObject, useState, useCallback } from 'react';
import { Popover, type PopoverAlign, type MenuPopoverPosition } from './Popover';
import { MENU_PANEL_CLASS, MENU_ROW_CLASS, MENU_ROW_HOVER_CLASS } from './menuPanelStyles';
import { DropdownCloseContext, useDropdownClose } from './dropdownClose';
import { Icon } from './Icon';
import { DisabledReasonMark, DEFAULT_DISABLED_REASON_TEST_ID } from './DisabledReasonMark';
import { iconMap } from '../../lib/iconMap';
import { cn } from '../../lib/utils';

export interface DropdownProps {
  trigger: ReactNode;
  /**
   * Position the panel against this element instead of against the trigger.
   *
   * For a trigger that is a small control inside a larger row the menu belongs to: aligning a
   * ~200px panel to a 24px button at the right edge of a sidebar pushes the panel off the screen
   * edge, whichever alignment is chosen. Point this at the row and the panel lands over it, while
   * the trigger stays the thing that opens it. The alignment then reads against the anchor.
   *
   * **The trigger must render inside the anchor.** `Popover` closes on a `mousedown` outside the
   * anchor, and this component's toggle fires on the later `click`: a trigger outside the anchor
   * therefore closes on mousedown and reopens on click, so the panel can never be dismissed from the
   * control that opened it. Nothing enforces the containment — it is a property of where the caller
   * renders the two.
   */
  anchorRef?: RefObject<HTMLElement | null>;
  align?: PopoverAlign;
  position?: MenuPopoverPosition;
  children: ReactNode;
  /**
   * Classes for the element wrapping the trigger — the `Popover`'s trigger box normally, the local
   * toggle in anchored mode. Both are the box around the trigger, so a width or layout class means
   * the same thing either way.
   */
  className?: string;
  /**
   * Controlled open state. Pass with {@link onOpenChange} when something other than the trigger has
   * to close the panel — a row whose own click navigates away, say, which `Popover` will not treat as
   * an outside click when the row is also the anchor. Leave unset and the component owns the state.
   */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  /**
   * Called once per opening, as soon as the panel can take focus: `Popover`'s `onShown`. For a
   * menu that moves focus to its first row when it opens, as a context menu opened from the keyboard must.
   */
  onShown?: () => void;
}

export function Dropdown({
  trigger,
  anchorRef,
  align = 'auto',
  position = 'auto',
  children,
  className = '',
  open: controlledOpen,
  onOpenChange,
  onShown,
}: DropdownProps) {
  const [uncontrolledOpen, setUncontrolledOpen] = useState(false);
  const open = controlledOpen ?? uncontrolledOpen;

  const handleOpenChange = useCallback((next: boolean) => {
    setUncontrolledOpen(next);
    onOpenChange?.(next);
  }, [onOpenChange]);

  const close = useCallback(() => handleOpenChange(false), [handleOpenChange]);

  // Anchored mode: `Popover` renders no trigger of its own when it positions against an external
  // element, so the toggle a trigger-mode Popover would provide is rendered here instead. It is also
  // the only thing that can close the panel from the trigger, which sits inside the anchor and is
  // therefore invisible to Popover's outside-click handler (see `anchorRef`).
  const panel = (
    <Popover
      trigger={anchorRef ? undefined : trigger}
      anchorRef={anchorRef}
      align={align}
      position={position}
      className={anchorRef ? undefined : className}
      triggerClassName={anchorRef ? undefined : 'w-full'}
      open={open}
      onOpenChange={handleOpenChange}
      panelClassName={MENU_PANEL_CLASS}
      onShown={onShown}
    >
      <DropdownCloseContext.Provider value={close}>
        {children}
      </DropdownCloseContext.Provider>
    </Popover>
  );

  if (!anchorRef) {
    return panel;
  }

  return (
    <>
      <div onClick={() => handleOpenChange(!open)} className={cn('cursor-pointer', className)}>
        {trigger}
      </div>
      {panel}
    </>
  );
}

export interface DropdownHeaderProps {
  children: ReactNode;
}

/** Small label rendered at the top of a dropdown panel. Use for short group titles
 *  like "Revert:" or "Filter by". */
export function DropdownHeader({ children }: DropdownHeaderProps) {
  return (
    <div className="px-3.5 py-1.5 text-body-sm text-muted">
      {children}
    </div>
  );
}

export interface DropdownItemProps {
  icon?: ReactNode;
  variant?: 'default' | 'danger';
  disabled?: boolean;
  onClick?: () => void;
  children: ReactNode;
  /**
   * Renders the item as a checkable row: a tick when true, and a reserved gap of the same width
   * when false so the labels of a group stay aligned.
   *
   * Exists because the two checkable groups in `OrganizationMemberRow` — access tier and
   * organization roles — had each hand-rolled the same pair: an `Icon` at `size={14}` and a bare
   * `<span className="w-3.5" />` spacer whose width silently depends on that size, with nothing
   * tying the two numbers together. Pass this instead of an `icon`; passing both lets `icon` win.
   *
   * `OrganizationSwitcher` renders a tick too and is deliberately not a caller: its tick trails the
   * label inside `children` while the icon slot already holds the organization glyph, so it is a
   * different control rather than a third copy of this one.
   */
  checked?: boolean;
  /**
   * Why this row cannot act, shown behind a ⓘ at the row's right edge (#2377).
   *
   * <b>Passing it DISABLES the row.</b> A reason on a row that still acts is a sentence nobody will
   * ever read and a control that contradicts it, so the two are one decision here rather than two
   * flags a call site has to keep agreeing. `disabled` still means what it meant — pass both when a
   * row is blocked for a reason AND busy, which is what an in-flight mutation on an already-refused
   * row looks like.
   *
   * <b>Which means the reason has to be CONDITIONAL, and passing one unconditionally silently removes
   * the command.</b> The coupling that saves a call site from two flags disagreeing is the same
   * coupling that turns `disabledReason="…"` into `disabled` forever — and it fails quietly, because a
   * permanently greyed row with a correct sentence beside it looks deliberate. Hand it
   * `blocked ? 'sentence' : undefined`, and let a test open the row in the state where it should ACT:
   * an assertion that the mark is absent catches this and an assertion that it is present cannot.
   * Found on #2351's Rename, where the unconditional form reddened the two rename-flow tests rather
   * than the gating ones — the popover never opened, which is what the bug actually is.
   *
   * <b>Not for a row that is merely busy.</b> `isPending`, `isStreaming`, `busy`: a row dead for
   * 400ms owes nobody an explanation, and a mark that flickers in and out is worse than silence.
   * This is for a refusal a person can do something about, or at least understand.
   *
   * The sentence is the whole content — no "Disabled:" prefix, no trailing "…is not available",
   * which is the part the reader already worked out by finding the row greyed.
   */
  disabledReason?: string;
  /**
   * The mark's own `data-testid`, when a menu holds several blocked rows a test has to tell apart.
   *
   * Exists because `DropdownItem` takes no rest props: the table menu's spec locates a mark per act
   * (`table-structure-reason-moveRowDown`) and counts marks by testid prefix, and neither is
   * expressible against one shared id. Defaults to {@link DEFAULT_DISABLED_REASON_TEST_ID}, which is
   * what a menu with a single blocked row wants.
   */
  disabledReasonTestId?: string;
  /**
   * The keyboard route to the same command — "Ctrl+Z" — drawn at the row's right edge the way a
   * desktop menu shows it (#2539).
   *
   * `aria-hidden`: it repeats the key a screen-reader user already reaches the command by, and spoken
   * after the label it turns "Undo" into "Undo Ctrl Z" on every pass through the menu. So the row's
   * accessible name stays its label alone.
   */
  shortcut?: string;
}

export function DropdownItem({
  icon,
  variant = 'default',
  disabled = false,
  onClick,
  checked,
  disabledReason,
  disabledReasonTestId = DEFAULT_DISABLED_REASON_TEST_ID,
  shortcut,
  children,
}: DropdownItemProps) {
  const closeDropdown = useDropdownClose();

  const handleClick = () => {
    onClick?.();
    closeDropdown?.();
  };

  // The hover fill each variant lights up with — suppressed for a disabled row by the
  // `disabled:hover:` below, which wins on specificity rather than on order: two pseudo-classes beat
  // one, so the suppression holds however Tailwind emits the pair. A row that cannot act must not
  // look live under the pointer, and before #2377 that only mattered on the handful of transiently
  // greyed rows nobody hovers; the mark made blocked rows something a person deliberately points at.
  // `DropdownOptionRow` has carried the same guard since it was written.
  const variantStyles = {
    default: `text-fg ${MENU_ROW_HOVER_CLASS}`,
    danger: 'text-danger-11 hover:bg-danger-a-3',
  };

  const iconStyles = {
    default: 'text-muted',
    danger: 'text-danger-11',
  };

  // The tick and its placeholder are one decision here, so the spacer can never drift from the
  // icon's size the way it does when each call site writes both.
  const checkMark =
    checked === undefined ? undefined : checked ? <Icon icon={iconMap.success} size={14} /> : <span className="w-3.5" />;

  const button = (
    <button
      type="button"
      disabled={disabled || disabledReason !== undefined}
      onClick={handleClick}
      // The shared menu row's inset pill (`MENU_ROW_CLASS`), which either variant's hover paints in.
      className={`w-full flex items-center gap-2.5 px-3.5 py-2 text-body-md transition-colors ${MENU_ROW_CLASS}
        ${variantStyles[variant]}
        disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent`}
    >
      {(icon ?? checkMark) && (
        <span className={`flex items-center shrink-0 [&_svg]:stroke-current ${iconStyles[variant]}`}>
          {icon ?? checkMark}
        </span>
      )}
      {shortcut === undefined ? children : (
        <>
          <span className="flex-1 text-left">{children}</span>
          <span aria-hidden className="ml-6 text-body-sm text-muted tabular-nums">{shortcut}</span>
        </>
      )}
    </button>
  );

  if (disabledReason === undefined) {
    return button;
  }

  // The mark is a SIBLING of the button rather than content inside it, and that is the whole
  // mechanism: a disabled button raises no pointer events, so a tooltip trigger placed inside one
  // can never fire. The flex line is what puts the mark beside the command while leaving the
  // command's own hit area alone — pointing at a row must not summon a paragraph.
  return (
    <div className="flex items-center">
      {button}
      <DisabledReasonMark reason={disabledReason} testId={disabledReasonTestId} />
    </div>
  );
}
