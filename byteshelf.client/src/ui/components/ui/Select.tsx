import { useState, useCallback, type ReactNode } from 'react';
import { cn } from '../../lib/utils';
import { Popover } from './Popover';
import { Icon } from './Icon';
import { iconMap } from '../../lib/iconMap';
import { DropdownOptionRow, type Option } from './DropdownOptionRow';
import { MENU_PANEL_CLASS } from './menuPanelStyles';
import { TOOLBAR_CONTROL_BOX_CLASS } from './toolbarControlStyles';
import { Squircle } from './Squircle';
import { fieldRendering } from './fieldStyles';
import { CONTROL_HEIGHT_CLASS } from './buttonStyles';
import { ErrorText } from './ErrorText';
import { useFieldError } from './useFieldError';
import type { MenuPopoverPosition } from '../../lib/popoverPlacement';

/**
 * A choice in a `Select`: a value and the label it is shown by. `T` narrows the value to a caller's own set of strings
 * (a union of names), as `PickerCard<T>` does, so the value `onChange` hands back is already of that set; left out, it
 * is any string, as it always was.
 */
export type SelectOption<T extends string = string> = Omit<Option, 'value'> & { value: T };

/**
 * Where the select is standing, which decides both how it is drawn and WHAT IT IS.
 *
 * - `field` — a form field: a filled surface with a form border, the shape every other control in a
 *   form has. The default, and what every caller rendered before the toolbar variant existed.
 * - `toolbar` — a control in a strip of controls: the shared toolbar box, transparent until opened,
 *   and a real `<button>` rather than the `div` below.
 *
 * <b>The trigger ELEMENT differs, and that is the substance of the variant rather than a detail of
 * it.</b> A toolbar declares `role="toolbar"`, which tells a screen reader that its children are
 * controls acting on the region below — a `div` among them is not a control, cannot be tabbed to and
 * cannot be pressed with a key, so the style dropdown would be the one item in the strip that a
 * person not using a mouse could not reach at all.
 *
 * <b>What that buys, stated exactly: the TRIGGER becomes reachable and operable, not the options.</b>
 * `Popover` moves no focus into the panel it opens, traps none there and restores none on Escape, and
 * the panel is portalled to `document.body` — so tabbing on from an open trigger walks the rest of
 * the page rather than the list. That is a property of `Popover`, shared with `Dropdown` and
 * `PickerCard` and older than this variant, so it is not a regression and not this component's to
 * fix; it is written down because the sentence above is easy to read as a bigger claim than it is.
 */
export type SelectVariant = 'field' | 'toolbar';

/**
 * How large the control is drawn — its text and the room beside it.
 *
 * `md` is what every caller rendered before this existed.
 *
 * <b>Independent of {@link SelectVariant}, and worth knowing that nothing yet uses the independence.</b>
 * Of the 23 call sites across both apps, exactly two pass `size`, and both pass `size="sm"` beside
 * `variant="toolbar"` — so two of the four cells in the matrix are unexercised, and one of those was
 * broken until it was measured: `toolbar` at the default `md` put a 20px line plus `py-2` inside a
 * 32px box and overran it. The two axes are kept apart because they were asked for apart and because
 * they genuinely answer different questions, but an unexercised cell is a cell nothing is defending —
 * which is why the vertical room moved to the surface and why both toolbar sizes are now tested.
 */
export type SelectSize = 'md' | 'sm';

export interface SelectProps<T extends string = string> {
  /** The chosen option's value. Any string: one that names no option draws the placeholder. */
  value: string;
  /** Called with the value of the option chosen, which is one of `options`' and so of `T`. */
  onChange: (value: T) => void;
  options: SelectOption<T>[];
  label?: string;
  placeholder?: string;
  error?: string;
  disabled?: boolean;
  /** Read {@link SelectVariant}. Defaults to the form field every existing caller renders. */
  variant?: SelectVariant;
  /** Read {@link SelectSize}. Defaults to the size every existing caller renders. */
  size?: SelectSize;
  /**
   * Toolbar variant only: labels the trigger reserves room for beyond the options it lists now. Read
   * {@link ToolbarLabelSizer} — a caller whose list empties in some state (the paragraph style with
   * the caret in no paragraph) passes its full list here, so the box does not jump as the list does.
   */
  reserveLabels?: readonly string[];
  /**
   * Which side of the trigger the list opens on, as `Popover` takes it; `auto`, the half of the screen the trigger is
   * not in, by default. A field's label sits above its trigger, so a list `auto` opens above covers the label it
   * belongs to: a field low in a tall form inside a panel, where the reader is choosing what that label names, passes
   * `below`, which keeps the label in sight and caps the list to the room under it. A menu's side never flips
   * (`MenuPopoverPosition`), so the list stays under the cursor that opened it.
   */
  position?: MenuPopoverPosition;
  className?: string;
}

/**
 * Everything both variants share. Deliberately states no colour, border, radius, height or padding.
 *
 * The radius belongs to the surface rather than here: the toolbar's comes from
 * `TOOLBAR_CONTROL_BOX_CLASS`, and stating one here too put `rounded-lg` on the element twice.
 */
const TRIGGER_BASE_CLASS = 'flex items-center justify-between w-full transition-shadow duration-200';

/**
 * How large the control is drawn — the type scale and the room beside the text.
 *
 * <b>HORIZONTAL ONLY, and that is not an oversight.</b> The vertical room belongs to the surface,
 * because only the surface knows whether the control has a definite height. A field grows to fit its
 * padding; the toolbar box does not, and `py-2` inside a 32px box put a 20px line plus 16px of
 * padding into it and overran the ring by two pixels at each end. No caller passed that combination,
 * which is exactly why it would have shipped.
 */
const SIZE_CLASS: Record<SelectSize, string> = {
  md: 'px-3 text-body-md',
  sm: 'px-2 text-body-sm',
};

/** A field's label, on the same type scale as its control: a small field's label in the page's own size reads as a heading. */
const LABEL_SIZE_CLASS: Record<SelectSize, string> = {
  md: 'text-body-md',
  sm: 'text-body-sm',
};

/**
 * The toolbar variant's surface, closed and open.
 *
 * <b>Each cell is the WHOLE set of surface utilities, not a delta on the closed one.</b> `cn`
 * concatenates rather than merges, so an open state written as an addition would leave both
 * backgrounds on the element and let stylesheet order pick the winner — the trap
 * `ordo.ui/src/lib/utils.ts` documents.
 *
 * The `field` variant is not here: it is the shared squircled field surface
 * ({@link fieldRendering}) that `Input` and `Textarea` paint, so a form's picker and its text fields
 * are one `panel`-white, grey-edged object rather than a grey box beside two fields. Its open state is
 * that surface's `open` look — the focus edge and glow a text field shows while it holds focus.
 */
const TOOLBAR_SURFACE_CLASS = {
  closed: `${TOOLBAR_CONTROL_BOX_CLASS} bg-transparent`,
  open: `${TOOLBAR_CONTROL_BOX_CLASS} bg-surface-active`,
};

/**
 * How the toolbar draws a refused value. Its outline is a ring rather than a border, so a
 * `border-danger-7` would be inert on it: Tailwind's preflight sets `border-width: 0` on every
 * element, and the toolbar surface adds no `border` utility for that colour to land on. The field
 * marks a refusal on its squircle's edge instead ({@link fieldRendering}'s `error`).
 */
const TOOLBAR_ERROR_CLASS = 'ring-danger-7';

/**
 * THE TOOLBAR TRIGGER IS AS WIDE AS ITS WIDEST LABEL — not the label shown now, and not a width the
 * caller guesses (#2539). A fixed width left half the box empty on the longest label; sizing to the
 * shown label would move every control after it whenever the value changed.
 *
 * Every label is stacked invisibly in one grid cell, so the cell takes the widest of them, and the
 * shown label is drawn over it. The labels are written through a pseudo-element's `content` rather
 * than as text, so they add nothing a reader, a screen reader or a text query in a test can find.
 *
 * `text-left` because the trigger is a `<button>`, which centres its text by default, and the shown
 * label now spans the whole cell — so anything shorter than the widest label (`Fit` beside `100%`)
 * was drawn in the middle of the box.
 */
function ToolbarLabelSizer({ labels, children }: { labels: readonly string[]; children: ReactNode }) {
  return (
    <span className="grid min-w-0 text-left">
      {labels.map((label, index) => (
        <span
          key={index}
          aria-hidden
          data-label={label}
          className="invisible [grid-area:1/1] whitespace-nowrap after:content-[attr(data-label)]"
        />
      ))}
      {children}
    </span>
  );
}

export function Select<T extends string = string>({
  value,
  onChange,
  options,
  label,
  placeholder = 'Select...',
  error,
  disabled = false,
  variant = 'field',
  size = 'md',
  reserveLabels = [],
  position = 'auto',
  className = '',
}: SelectProps<T>) {
  const [open, setOpen] = useState(false);
  const { isInvalid, errorId, controlProps } = useFieldError({ error });

  // What the trigger reads: the selection, else nothing — which is the placeholder's cue below.
  // Resolved once so the text and the weight it is drawn at cannot disagree.
  const shownLabel = options.find((o) => o.value === value)?.label;

  const handleSelect = useCallback((optionValue: T) => {
    onChange(optionValue);
    setOpen(false);
  }, [onChange]);


  // The open look follows what Popover is actually given, so a disabled field cannot draw focus.
  const field = variant === 'field'
    ? fieldRendering({ error: isInvalid, open: !disabled && open })
    : null;

  const triggerClassName = cn(
    TRIGGER_BASE_CLASS,
    SIZE_CLASS[size],
    // A default field is as tall as the text field and button beside it; a small one keeps its padding.
    field ? `${size === 'md' ? CONTROL_HEIGHT_CLASS : 'py-2'} ${field.className}` : open ? TOOLBAR_SURFACE_CLASS.open : TOOLBAR_SURFACE_CLASS.closed,
    !field && 'gap-1.5',
    disabled ? 'opacity-40 cursor-not-allowed' : 'cursor-pointer',
    isInvalid && !field && TOOLBAR_ERROR_CLASS,
    className,
  );

  const triggerContent = (
    <>
      {field && <Squircle {...field.squircle} />}
      {/* The unfilled state is a PLACEHOLDER, so it takes the faint step at the same opacity
          `Input` and `Textarea` give theirs (`placeholder:text-neutral-9/50`) — a real `::placeholder`
          there, a rendered span here, and a form showing both should not have them at two weights.
          It was `neutral-10`, which matched neither. */}
      {/* TRUNCATED IN THE TOOLBAR AND NOWHERE ELSE. A toolbar control states a height, so a label
          longer than the control has to be cut or it is drawn straight over the document below —
          the same clipping-band argument the toolbar's own layout makes. A field grows instead: it
          wraps to a second line today, and turning that into an ellipsis is a change to every one of
          this component's callers, which is not what adding a variant is allowed to do. */}
      {variant === 'toolbar' ? (
        <ToolbarLabelSizer labels={[...options.map((o) => o.label), ...reserveLabels, shownLabel ?? placeholder]}>
          <span className={cn('[grid-area:1/1] truncate', shownLabel ? 'text-fg' : 'text-neutral-9/50')}>
            {shownLabel ?? placeholder}
          </span>
        </ToolbarLabelSizer>
      ) : (
        <span className={shownLabel ? 'text-fg' : 'text-neutral-9/50'}>
          {shownLabel ?? placeholder}
        </span>
      )}
      <Icon
        icon={iconMap.chevronDown}
        size={16}
        className={cn('text-muted shrink-0 transition-transform duration-200', open && 'rotate-180')}
      />
    </>
  );

  // The refusal is stated on the trigger in both variants, through the one wiring `Input` and
  // `Textarea` use. Only the toolbar's `<button>` is announced with it: the field's `div` takes no
  // focus and carries no role, so a screen reader never lands on it to read either attribute. They
  // are put there anyway so the statement travels with the element when it becomes a control.
  //
  // A fixed hook for tests, on whichever element is the trigger. Fixed rather than a passthrough
  // prop: every caller wants the same thing, and threading one through would put the burden on each
  // of them. It was introduced when the trigger was only ever a `div` — carrying no role a test could
  // target — and stays on the button for the same reason the tests that use it stay written as they
  // are, which is that they are about the control and not about the tag it happens to render as.
  const trigger = variant === 'toolbar' ? (
    <button
      type="button"
      data-testid="select-trigger"
      className={triggerClassName}
      disabled={disabled}
      // Says the control expands something, which is true and is what a keyboard user needs to hear.
      // `aria-haspopup` is deliberately ABSENT: its values name what the popup IS, and this panel is
      // a stack of plain buttons rather than a `listbox` or a `menu` — claiming either would send a
      // screen reader looking for semantics and navigation keys that are not there. Mirrors the open
      // state Popover is actually given rather than the raw one, so a disabled control cannot announce
      // itself expanded.
      aria-expanded={disabled ? false : open}
      {...controlProps}
    >
      {triggerContent}
    </button>
  ) : (
    <div data-testid="select-trigger" className={triggerClassName} {...controlProps}>
      {triggerContent}
    </div>
  );

  return (
    <div className="flex flex-col gap-2 w-full">
      {label && (
        <span className={`${LABEL_SIZE_CLASS[size]} font-medium text-fg`}>
          {label}
        </span>
      )}
      <Popover
        trigger={trigger}
        open={disabled ? false : open}
        onOpenChange={disabled ? undefined : setOpen}
        panelClassName={MENU_PANEL_CLASS}
        position={position}
        triggerClassName="w-full"
        className="w-full"
      >
        {options.map((option) => (
          <DropdownOptionRow
            key={option.value}
            label={option.label}
            trailing={option.trailing}
            selected={option.value === value}
            onClick={() => handleSelect(option.value)}
          />
        ))}
      </Popover>
      {error && (
        <ErrorText id={errorId}>{error}</ErrorText>
      )}
    </div>
  );
}
