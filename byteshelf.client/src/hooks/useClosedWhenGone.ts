import { useEffect, useRef, type RefObject } from 'react';

interface Held<T> {
  /** What is open: each put in as it opens, and taken out as it closes by itself. */
  open: Set<T>;
  /** Whether the component is gone, so nothing is to open any more. */
  isGone: boolean;
}

/** Holds what a component opened, closed once the component is gone. */
export function useClosedWhenGone<T extends { close: () => void }>(): RefObject<Held<T>> {
  const held = useRef<Held<T>>({ open: new Set(), isGone: false });
  useEffect(() => {
    const { current } = held;
    current.isGone = false;
    return () => {
      current.isGone = true;
      current.open.forEach((each) => each.close());
    };
  }, []);
  return held;
}
