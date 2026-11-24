import { Suspense } from 'react';
import { Icon, iconMap } from '../ui';
import type { FolderType } from '../lib/plugins/folderType';
import { ErrorBoundary } from './ErrorBoundary';

/** A folder's icon: its type's, or a plain one's, as while the type's loads or when it cannot be drawn. */
export function FolderIcon({ type, size, className }: { type: FolderType | null; size: number; className?: string }) {
  const plain = <Icon icon={iconMap.folder} size={size} className={className} />;
  if (!type) return plain;
  return (
    <ErrorBoundary key={type.suffix} fallback={plain}>
      <Suspense fallback={plain}><Icon icon={type.icon} size={size} className={className} /></Suspense>
    </ErrorBoundary>
  );
}
