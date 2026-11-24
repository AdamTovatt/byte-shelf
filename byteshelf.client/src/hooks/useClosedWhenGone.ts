import { useEffect, useState } from 'react';

/** A set whose members are closed once the component is gone: each put in as it opens, and taken out as it closes by itself. */
export function useClosedWhenGone<T extends { close: () => void }>(): Set<T> {
  const [open] = useState(() => new Set<T>());
  useEffect(() => () => open.forEach((each) => each.close()), [open]);
  return open;
}
