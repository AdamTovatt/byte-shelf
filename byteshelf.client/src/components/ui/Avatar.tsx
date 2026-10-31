import { type ImgHTMLAttributes } from 'react';

type AvatarSize = 'xs' | 'sm' | 'md' | 'lg' | 'xl';
type AvatarStatus = 'online' | 'offline' | 'busy';

export interface AvatarProps extends Omit<ImgHTMLAttributes<HTMLImageElement>, 'size'> {
  size?: AvatarSize;
  name?: string;
  status?: AvatarStatus;
}

const sizeStyles: Record<AvatarSize, string> = {
  xs: 'w-6 h-6 text-xs',
  sm: 'w-8 h-8 text-sm',
  md: 'w-10 h-10 text-base',
  lg: 'w-12 h-12 text-lg',
  xl: 'w-16 h-16 text-xl',
};

const statusSizeStyles: Record<AvatarSize, string> = {
  xs: 'w-2 h-2 border',
  sm: 'w-2.5 h-2.5 border-[1.5px]',
  md: 'w-3 h-3 border-2',
  lg: 'w-3.5 h-3.5 border-2',
  xl: 'w-4 h-4 border-2',
};

const statusColorStyles: Record<AvatarStatus, string> = {
  online: 'bg-success-light dark:bg-success-dark',
  offline: 'bg-text-3-light dark:bg-text-3-dark',
  busy: 'bg-danger-light dark:bg-danger-dark',
};

function getInitials(name: string): string {
  return name
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0].toUpperCase())
    .join('');
}

export function Avatar({
  size = 'md',
  name,
  status,
  src,
  alt,
  className = '',
  ...props
}: AvatarProps) {
  const initials = name ? getInitials(name) : '';

  return (
    <div className={`relative inline-flex shrink-0 ${className}`}>
      {src ? (
        <img
          src={src}
          alt={alt || name || 'Avatar'}
          className={`${sizeStyles[size]} rounded-full object-cover ring-1 ring-border-light/50 dark:ring-border-dark/50`}
          {...props}
        />
      ) : (
        <div
          className={`${sizeStyles[size]} rounded-full flex items-center justify-center font-medium bg-brand-surface-light text-brand-tint-light dark:bg-brand-surface-dark dark:text-brand-tint-dark`}
        >
          {initials}
        </div>
      )}
      {status && (
        <span
          className={`absolute bottom-0 right-0 rounded-full border-elevation-2-light dark:border-elevation-2-dark ${statusSizeStyles[size]} ${statusColorStyles[status]}`}
        />
      )}
    </div>
  );
}
