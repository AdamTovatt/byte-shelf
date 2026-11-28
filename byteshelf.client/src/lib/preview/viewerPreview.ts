import { createContext, useCallback, useContext, useEffect, useEffectEvent, useMemo, useState, type RefObject } from 'react';
import { pressesTarget, takesTyping } from '../keys';

/** A viewer's handler of a key pressed in its preview; `preventDefault` takes it from the preview's own. */
export type PreviewKeyHandler = (event: KeyboardEvent) => void;

/** Whether the preview can make the page full screen, whether it is, and going there or back. */
export interface FullScreen {
  canFullScreen: boolean;
  isFullScreen: boolean;
  toggle: () => void;
}

/**
 * What a viewer reaches of the preview it is in: the slot of the bottom bar its controls go in (`PreviewControls`),
 * `addControls` counting one drawing them there and answering what counts them gone; `addKeys` handing a handler the
 * keys the preview hears until what it answers is called; and full screen of the page.
 */
export interface ViewerPreview {
  slot: HTMLElement | null;
  addControls: () => () => void;
  addKeys: (handler: PreviewKeyHandler) => () => void;
  fullScreen: FullScreen;
}

const ViewerPreviewContext = createContext<ViewerPreview | null>(null);

/** Gives what is drawn in a preview the preview it is in (`useViewerPreviewHost`). */
export const ViewerPreviewProvider = ViewerPreviewContext.Provider;

/** The preview the viewer is in; null outside one. */
export function useViewerPreview(): ViewerPreview | null {
  return useContext(ViewerPreviewContext);
}

/** What a preview gives its viewer (`preview`), with what is the preview's own of it. */
export interface ViewerPreviewHost {
  preview: ViewerPreview;
  /** Sets the element of the bottom bar the viewer's controls are drawn in. */
  setSlot: (slot: HTMLElement | null) => void;
  /** Whether a viewer draws controls there. */
  hasControls: boolean;
  /**
   * Hands a key the preview hears, as the overlay on top, to the viewer's handlers, unless it was typed into a field,
   * presses the button that has the focus, or something took it already.
   */
  offer: (event: KeyboardEvent) => void;
}

/**
 * A preview's of what it gives its viewer. While it is open the page may go full screen, the menus and dialogs over
 * it too, where the browser makes an element full screen; closing it takes the page out of full screen. A browser
 * that refuses either is left as it is.
 */
export function useViewerPreviewHost(): ViewerPreviewHost {
  const [handlers] = useState(() => new Set<PreviewKeyHandler>());
  const [slot, setSlot] = useState<HTMLElement | null>(null);
  const [controls, setControls] = useState(0);
  const [isFullScreen, setIsFullScreen] = useState(isPageFullScreen);

  const addControls = useCallback(() => {
    setControls((count) => count + 1);
    return () => setControls((count) => count - 1);
  }, []);
  const addKeys = useCallback((handler: PreviewKeyHandler) => {
    handlers.add(handler);
    return () => void handlers.delete(handler);
  }, [handlers]);
  const offer = useCallback((event: KeyboardEvent) => {
    for (const handler of handlers) {
      if (event.defaultPrevented || takesTyping(event.target) || pressesTarget(event)) return;
      handler(event);
    }
  }, [handlers]);

  useEffect(() => {
    const onChange = () => setIsFullScreen(isPageFullScreen());
    document.addEventListener('fullscreenchange', onChange);
    return () => {
      document.removeEventListener('fullscreenchange', onChange);
      if (isPageFullScreen()) void document.exitFullscreen().catch(() => {});
    };
  }, []);

  const fullScreen = useMemo<FullScreen>(() => ({
    canFullScreen: document.fullscreenEnabled,
    isFullScreen,
    toggle: () => {
      if (document.fullscreenElement) void document.exitFullscreen().catch(() => {});
      else if (document.fullscreenEnabled) void document.documentElement.requestFullscreen().catch(() => {});
    },
  }), [isFullScreen]);
  const preview = useMemo(() => ({ slot, addControls, addKeys, fullScreen }), [slot, addControls, addKeys, fullScreen]);
  return { preview, setSlot, hasControls: controls > 0, offer };
}

/**
 * Hands `handler` the keys pressed in the preview the viewer is in while that is the overlay on top, those pressed
 * with a modifier too (`isPlainKey` tells them): none while a dialog is over it, none typed into a field (a slider is
 * not one), none that press the button that has the focus, as Space and Enter do, and none taken already, as by a
 * control's own handler; never Escape or Tab, which are the overlay's. `handler` hears them ahead of the preview,
 * whose arrow keys step between files unless it calls `preventDefault`. Outside a preview, none.
 */
export function usePreviewKeys(handler: PreviewKeyHandler): void {
  const addKeys = useViewerPreview()?.addKeys;
  const onKey = useEffectEvent(handler);
  useEffect(() => addKeys?.((event) => onKey(event)), [addKeys]);
}

/**
 * Full screen of the page while the viewer's preview is open, bars, menus and dialogs and all (`useViewerPreviewHost`).
 * Where the browser makes no element full screen, as on an iPhone, `video`'s own full screen in the browser's player
 * instead, when it is given one, and else none; one that refuses is left as it is. Outside a preview, none.
 */
export function usePreviewFullScreen(video?: RefObject<HTMLVideoElement | null>): FullScreen {
  const fullScreen = useViewerPreview()?.fullScreen;
  if (!fullScreen) return NONE;
  if (fullScreen.canFullScreen || video === undefined || !('webkitEnterFullscreen' in HTMLVideoElement.prototype)) return fullScreen;
  return {
    canFullScreen: true,
    isFullScreen: false,
    toggle: () => {
      try {
        (video.current as { webkitEnterFullscreen?: () => void } | null)?.webkitEnterFullscreen?.();
      } catch {
        // Refused, as before the video has loaded anything.
      }
    },
  };
}

const NONE: FullScreen = { canFullScreen: false, isFullScreen: false, toggle: () => {} };

/** Whether the page is full screen, as a preview makes it; another element full screen is not. */
function isPageFullScreen(): boolean {
  return document.fullscreenElement === document.documentElement;
}
