import type { ReactNode, Ref } from 'react';

/** The place at a row's start of what stands for its file or folder. */
export function RowIcon({ ref, children }: { ref?: Ref<HTMLSpanElement>; children: ReactNode }) {
  return <span ref={ref} className="flex size-8 shrink-0 items-center justify-center">{children}</span>;
}
