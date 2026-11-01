import type { ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { iconMap } from '../../lib/iconMap';
import { IconButton } from './IconButton';
import { OverlayDepthContext, useOverlay } from '../../hooks/useOverlay';
import { cn } from '../../lib/utils';
import { Card } from './Card';
import { CustomScroll } from './CustomScroll';

const modalBg = 'bg-canvas dark:bg-panel/80 dark:backdrop-blur-sm';

interface ModalBaseProps {
  isOpen: boolean;
  onClose: () => void;
  children: ReactNode;
  className?: string;
  /**
   * When false, nothing dismisses the dialog from outside its own content: Escape and the backdrop are
   * inert and the header's close button is not rendered. Use it while an action is in flight, or for a
   * dialog whose content cannot be recovered once it is gone (a secret shown exactly once) — the caller
   * then owns the only exit, and calls `onClose` itself from a button in the body. Default true.
   *
   * The close button is *hidden* rather than left inert on purpose: a control that looks like the way out
   * and does nothing is a worse answer than no control. The prop name and the reason for blocking are taken
   * from {@link QuickPrompt}, which locks the same dismissal channels (and which {@link ConfirmDelete}
   * drives from its in-flight state) — the hiding half is this component's own, since a popover has no
   * close button to hide.
   */
  dismissible?: boolean;
  /**
   * Marks the rendered dialog card for a test to address — an e2e capture above all, which has to scope
   * to the dialog rather than the viewport: a modal portals into `document.body` over a translucent
   * backdrop, so a full-viewport shot composites whatever the page behind it happens to hold, and those
   * are pixels no mask can remove because they are inside the frame by construction.
   *
   * It lands on the card, so the marked element is the whole dialog including its header. A caller that
   * needs a *narrower* frame than that still marks its own wrapper, a shot of the body alone,
   * which is the case this prop deliberately does not try to cover:
   * one marker per element, placed by whoever knows which element is wanted.
   */
  testId?: string;
}

/** A dialog in a card: a header with the title and a close button, over a scrolling body. */
interface FramedModalProps extends ModalBaseProps {
  variant?: 'centered' | 'fullscreen';
  title?: string;
  /**
   * Lets the title wrap onto further lines rather than truncating. For a modal whose title is content
   * the reader must see whole — `ReadMoreModal`, titled with the short line it expands — rather than a
   * label the body repeats. Default false: every other title stays on one line.
   */
  wrapTitle?: boolean;
  headerActions?: ReactNode;
  disableContentScroll?: boolean;
  disableContentPadding?: boolean;
}

/**
 * `bare` draws no card and no header: the children stand straight on the backdrop, centred. For content
 * that brings its own chrome — an embedded page of another product, such as Microsoft's SharePoint File
 * picker — where a frame of the app's around it would be a second window border.
 *
 * With no header there is nothing to show a title in, so `title` is the dialog's only name, and required;
 * and the header's and body's options do not apply, so they do not type-check here. Escape and the
 * backdrop close it — Escape only while focus is in the app's page: a key pressed inside a cross-origin
 * frame is that frame's document's, and never reaches this one's listener. The backdrop always works.
 */
interface BareModalProps extends ModalBaseProps {
  variant: 'bare';
  title: string;
  wrapTitle?: never;
  headerActions?: never;
  disableContentScroll?: never;
  disableContentPadding?: never;
}

export type ModalProps = FramedModalProps | BareModalProps;

/** The dismissal a non-dismissible modal gets: the channels stay wired, and doing nothing is the answer. */
function noDismiss() {}

// The title takes the width the actions and the close button leave, rather than a fixed share of the
// row. This was `1fr_auto_1fr` (#2326): two matched side columns, which centred the middle one and
// charged the title half the header for it — so a title truncated against space the close button
// never used. It cost four ordo.client confirmation dialogs the end of the question being confirmed
// and the extension of the file it applied to, and this branch's unlock dialog its last word.
//
// Centring was never a requirement, only what that template happened to do, so it is gone rather than
// made conditional: one shape for every titled modal, and the caller with the longest title in the app
// (`CompliancePackPreviewModal`) stops paying the most for it. Its actions now sit beside the close
// button instead of mid-header.
//
// `minmax(0,...)` rather than `1fr` on the title says in the template that the title is the column
// allowed to shrink. It already carries `truncate` (or wraps, under `wrapTitle`), so this rests on
// nothing but itself.
const headerColumns = 'grid-cols-[minmax(0,1fr)_auto_auto]';

/**
 * What every variant shares: the portal, the full-screen layer that centres the dialog, and the backdrop
 * that dismisses it. The dialog itself is the caller's, which stops its own clicks from reaching the
 * backdrop.
 *
 * **One dimming layer however many dialogs are open** (#2444). A modal opened from inside another — the
 * SharePoint connect prompt and picker from the new-template dialog, a confirmation from a form — keeps its
 * backdrop, which still takes the click that dismisses it, but paints it clear: the outer modal's is
 * already over the page, and a second one stacked on it darkened the dialog the person had just been
 * using into a black slab behind the new one. `nested` is read from the React tree
 * ({@link OverlayDepthContext}) because both portal into `document.body`.
 */
function ModalShell({ dismiss, gutter, backdropClassName, nested, children }: { dismiss: () => void; gutter: string; backdropClassName?: string; nested: boolean; children: ReactNode }) {
  return createPortal(
    <div className={cn('fixed inset-0 z-50 flex items-center justify-center', gutter)}>
      <div
        // Not stated and then overridden: `cn` concatenates, so a clear class beside `bg-black/80` would be
        // decided by the stylesheet's order. The variant's own backdrop colour is a dimming too, and goes.
        className={cn('fixed inset-0 animate-modal-backdrop', nested ? 'bg-transparent' : ['bg-black/80', backdropClassName])}
        data-testid="modal-backdrop"
        onClick={dismiss}
      />
      {children}
    </div>,
    document.body,
  );
}

const slideIn = 'animate-[modalSlideIn_0.3s_cubic-bezier(0.25,0.1,0.25,1)]';

function ModalHeader({ onClose, title, wrapTitle, headerActions, dismissible }: { onClose: () => void; title?: string; wrapTitle: boolean; headerActions?: ReactNode; dismissible: boolean }) {
  if (title) {
    return (
      <div className={cn('grid items-center px-5 py-2 border-b border-border', headerColumns)}>
        <h2 className={cn('text-heading-sm text-fg my-2', wrapTitle ? 'break-words' : 'truncate')}>{title}</h2>
        <div className="flex items-center">{headerActions}</div>
        {dismissible && <IconButton variant="ghost" size="sm" icon={iconMap.close} onClick={onClose} aria-label="Close" className="justify-self-end" />}
      </div>
    );
  }

  if (!dismissible) return null;

  return (
    <IconButton variant="ghost" size="sm" icon={iconMap.close} onClick={onClose} aria-label="Close" className="absolute top-3 right-3 z-20" />
  );
}

export function Modal(props: ModalProps) {
  const { isOpen, onClose, title, children, className = '', dismissible = true, testId } = props;
  // The dismissal channels all route through one callback, so blocking them is blocking that callback —
  // which is why this is derived once here rather than guarded at each of the three call sites below.
  const dismiss = dismissible ? onClose : noDismiss;
  // Escape is heard here, on the app's document: see `BareModalProps` for the frame it cannot hear inside.
  const nestedOverlayDepth = useOverlay({ isOpen, onClose: dismiss });
  // This modal's own depth: above 1, another modal encloses it, and that one's backdrop is the dimming.
  const nested = nestedOverlayDepth > 1;

  if (!isOpen) return null;

  // The body is scoped so an overlay opened from inside this one registers as nested rather than as
  // a sibling — which is what decides Escape between the two, and cannot be read off the DOM because
  // both portal into `document.body`. Wrapped once and rendered by every variant, so they cannot come
  // to disagree about it.
  const scopedChildren = (
    <OverlayDepthContext.Provider value={nestedOverlayDepth}>{children}</OverlayDepthContext.Provider>
  );

  if (props.variant === 'bare') {
    return (
      <ModalShell dismiss={dismiss} gutter="p-4" nested={nested}>
        <div
          role="dialog"
          aria-modal="true"
          aria-label={title}
          className={cn('z-10', slideIn, className)}
          data-testid={testId}
          onClick={(e) => e.stopPropagation()}
        >
          {scopedChildren}
        </div>
      </ModalShell>
    );
  }

  const { wrapTitle = false, headerActions, disableContentScroll = false, disableContentPadding = false, variant = 'centered' } = props;
  const header = <ModalHeader onClose={onClose} title={title} wrapTitle={wrapTitle} headerActions={headerActions} dismissible={dismissible} />;

  if (variant === 'centered') {
    return (
      <ModalShell dismiss={dismiss} gutter="p-4" nested={nested}>
        <Card
          padding="none"
          defaultBackground={false}
          className={cn('z-10 w-full max-w-lg max-h-[90vh] shadow-login-card flex flex-col', slideIn, modalBg, className)}
          role="dialog"
          aria-modal="true"
          aria-label={title}
          data-testid={testId}
          onClick={(e) => e.stopPropagation()}
        >
          {header}
          {/* The body that scrolls, for a capture to put at its top before framing the card (#2514). A
              fixed id rather than one per caller: it is looked up inside a card, which several open
              modals each have one of, so it never has to tell two dialogs apart on its own. */}
          <div data-testid="modal-body" className={cn('flex-1', disableContentScroll ? 'overflow-hidden flex flex-col' : 'overflow-y-auto', !disableContentPadding && 'p-5')}>{scopedChildren}</div>
        </Card>
      </ModalShell>
    );
  }

  return (
    <ModalShell dismiss={dismiss} gutter="p-6" backdropClassName="dark:bg-black/60" nested={nested}>
      <Card
        padding="none"
        defaultBackground={false}
        className={cn('z-10 w-full h-full shadow-login-card flex flex-col', slideIn, modalBg, className)}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        data-testid={testId}
        onClick={(e) => e.stopPropagation()}
      >
        {header}
        {disableContentScroll ? (
          <div className={cn('flex-1 min-h-0 overflow-hidden flex flex-col', !disableContentPadding && 'p-5')}>{scopedChildren}</div>
        ) : (
          <CustomScroll flex="1 1 auto" heightRelativeToParent="100%" className={cn('min-h-0', !disableContentPadding && 'p-5')}>
            {scopedChildren}
          </CustomScroll>
        )}
      </Card>
    </ModalShell>
  );
}
