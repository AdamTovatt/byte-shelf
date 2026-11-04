import { Icon } from './Icon';
import { Tooltip } from './Tooltip';
import { iconMap } from '../../lib/iconMap';
import { DISABLED_REASON_TOOLTIP_MAX_WIDTH } from './menuPanelStyles';

/** What a blocked row's mark is called when the caller does not name it. */
export const DEFAULT_DISABLED_REASON_TEST_ID = 'menu-item-reason';

export interface DisabledReasonMarkProps {
  /** The sentence a person reads. Written for somebody who has just pointed at a row that will not act. */
  reason: string;
  /** The row's own hook, so a menu of several blocked rows can be addressed one at a time. */
  testId: string;
  /**
   * REPLACES the default padding (`p-1.5 pr-3`, sized for a menu's right edge) rather than joining it,
   * because `cn` concatenates and two `pr-*` classes are decided by stylesheet order. For a mark beside a
   * control in a row of controls, where the extra right padding would widen the gap to the next one.
   * Keep the target at least `p-1.5` all round: the padding is the hit box (below).
   */
  paddingClassName?: string;
}

/**
 * The ⓘ beside a blocked menu row or a disabled control outside a menu, and the only place its reason
 * is written. The right padding is sized for a menu's edge; `paddingClassName` replaces it beside a
 * control.
 *
 * <b>The reason lives behind a mark, and the two versions before this one are why</b> — this began
 * as `TableStructureMenu`'s local `RefusalMark` (#2275) and is here because three menus wanted it.
 * First the reason was a native `title` on the wrapper around a disabled button: computed correctly,
 * asserted by reading the attribute, and never once shown to anybody, because a disabled control is
 * precisely what a browser will not raise a native tooltip for. Then it was a line of prose in the
 * panel, which showed it and cost something else: a dropdown is shrink-to-fit, so the panel became
 * as wide as the longest SENTENCE — a menu of six short commands drawn at the width of a paragraph.
 * The mark puts the width back on the longest LABEL and keeps the words one deliberate hover away.
 *
 * <b>Named, because a tooltip alone reaches one kind of reader.</b> The sentence is the element's
 * accessible name, so it is announced rather than merely drawn — and a portalled panel is read in
 * browse mode without focus, which is what makes that reach a screen reader at all. `role` is `img`
 * rather than `button`: it performs nothing, and calling it a button would promise an act on a row
 * whose act is refused.
 *
 * <b>`tabIndex` is here for the day #2378 lands, and reaches nobody today.</b> `Popover` portals its
 * panel to `document.body` and moves no focus into it, so tabbing on from an open trigger walks the
 * rest of the page rather than the menu: there is no keyboard route to this element, whatever the
 * attribute suggests. It is kept because `Tooltip` shows on focus as well as on hover and the
 * attribute is what will make that work the moment the panel is reachable — not because it works
 * now. The claim this comment replaces said the opposite, and was wrong when it was written.
 *
 * <b>The padding IS the control.</b> The glyph is 14px, which is not a thing to ask anybody to point
 * at; `p-1.5` around it makes the target 26px while the mark stays small. That is asserted in a
 * browser rather than trusted to this comment, because a class name is not a hit box.
 *
 * <b>It does not fade with the row it belongs to.</b> The label greys because it cannot be acted on;
 * the mark is the one thing on that row which still does something, so it is drawn at full strength.
 */
export function DisabledReasonMark({ reason, testId, paddingClassName = 'p-1.5 pr-3' }: DisabledReasonMarkProps) {
  return (
    <Tooltip content={reason} position="top" maxWidth={DISABLED_REASON_TOOLTIP_MAX_WIDTH}>
      <span
        role="img"
        tabIndex={0}
        aria-label={reason}
        data-testid={testId}
        className={`flex items-center ${paddingClassName} text-muted`}
      >
        <Icon icon={iconMap.info} size={14} />
      </span>
    </Tooltip>
  );
}
