import { useCallback, useRef, useState } from 'react';

/**
 * Measure an element's layout size with a `ResizeObserver`, via a **callback ref**. Returns the ref to
 * attach and the current `width`/`height`.
 *
 * Callback ref (not `useRef` + effect) so it measures whenever the element actually attaches — required
 * for portaled / conditionally-rendered nodes (e.g. a Popover's content, which mounts only when open,
 * after the consuming component's first render) where an effect-with-empty-deps would run too early and
 * never see the node. Works equally for elements present at mount (e.g. a Squircle's own `<svg>`).
 *
 * Reports the **layout** box (`borderBoxSize`), which is unaffected by ancestor CSS transforms — exactly
 * what squircle path coordinates need. The synchronous `getBoundingClientRect` on attach avoids a
 * one-frame unmeasured flash; the observer's first delivery then corrects it if it differed.
 */
export function useMeasuredSize<T extends Element = HTMLElement>(): {
  ref: (node: T | null) => void;
  width: number;
  height: number;
} {
  const [size, setSize] = useState<{ width: number; height: number }>({ width: 0, height: 0 });
  const observerRef = useRef<ResizeObserver | null>(null);

  const ref = useCallback((node: T | null) => {
    observerRef.current?.disconnect();
    observerRef.current = null;
    if (!node) return;

    const rect = node.getBoundingClientRect();
    setSize({ width: rect.width, height: rect.height });

    const observer = new ResizeObserver((entries) => {
      const box = entries[0]?.borderBoxSize?.[0];
      if (box) {
        setSize({ width: box.inlineSize, height: box.blockSize });
      } else {
        const contentRect = entries[0]?.contentRect;
        if (contentRect) setSize({ width: contentRect.width, height: contentRect.height });
      }
    });
    observer.observe(node);
    observerRef.current = observer;
  }, []);

  return { ref, width: size.width, height: size.height };
}
