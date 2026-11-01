import { useCallback, useEffect, useRef, useState } from 'react';
import { iconMap } from '../lib/iconMap';

export type CopyFeedbackState = 'idle' | 'copied' | 'failed';

const COPY_FEEDBACK_ICON: Record<CopyFeedbackState, typeof iconMap.copy> = {
  idle: iconMap.copy,
  copied: iconMap.success,
  failed: iconMap.alert,
};

const COPY_FEEDBACK_STATUS: Record<Exclude<CopyFeedbackState, 'idle'>, string> = {
  copied: 'Copied!',
  // The clipboard is offered only on HTTPS and localhost, so on a plain-HTTP server copying fails: say
  // what to do instead.
  failed: 'Could not copy: select it and press Ctrl+C',
};

export interface CopyFeedback {
  /** Current phase of the copy affordance: idle → copied / failed → (auto-reset) idle. */
  state: CopyFeedbackState;
  /** Write `text` to the clipboard, then flip to copied (or failed) and auto-reset to idle. */
  copy: (text: string) => Promise<void>;
  /** Icon for the current phase: copy → success (copied) → alert (failed). */
  icon: typeof iconMap.copy;
  /**
   * The copied / failed feedback text ("Copied!" / "Copy failed"), empty while idle. Drives both
   * the non-idle tooltip and an sr-only `role="status"` live region so the two stay in lockstep.
   */
  statusText: string;
}

/**
 * The shared copy-to-clipboard feedback state machine behind every copy button in the UI library
 * (CopyField's inline button, CodeBlock's header + hover buttons, the text-document preview's hover
 * button). Owns the idle → copied / failed → idle transition, the auto-reset timer, and its unmount
 * cleanup; call sites own only their own layout, idle label, and which text to copy.
 */
export function useCopyFeedback(resetDelayMs = 1500): CopyFeedback {
  const [state, setState] = useState<CopyFeedbackState>('idle');
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, []);

  const copy = useCallback(async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setState('copied');
    } catch {
      setState('failed');
    }
    if (timerRef.current) clearTimeout(timerRef.current);
    timerRef.current = setTimeout(() => setState('idle'), resetDelayMs);
  }, [resetDelayMs]);

  return {
    state,
    copy,
    icon: COPY_FEEDBACK_ICON[state],
    statusText: state === 'idle' ? '' : COPY_FEEDBACK_STATUS[state],
  };
}
