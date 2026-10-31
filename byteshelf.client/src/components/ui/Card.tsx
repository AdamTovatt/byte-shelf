import { type ReactNode, type HTMLAttributes, Children, Fragment, forwardRef, createContext, useContext } from 'react';
import { Divider } from './Divider';

type CardTint = 'info' | 'success' | 'warning' | 'danger';

export interface CardProps extends HTMLAttributes<HTMLDivElement> {
  variant?: 'flat' | 'transparent' | 'elevated' | 'interactive';
  padding?: 'none' | 'sm' | 'md' | 'lg';
  /** Size preset controlling border radius and padding scale. Default: 'lg'. */
  size?: 'sm' | 'md' | 'lg';
  /** Semantic color tint — replaces the default background with a translucent status color. */
  tint?: CardTint;
  /** When false, the default background is omitted so the caller can supply their own. */
  defaultBackground?: boolean;
  /** When true, the background is translucent with backdrop blur. */
  translucent?: boolean;
  children: ReactNode;
}

const solidBg = 'bg-elevation-2-light dark:bg-elevation-2-dark';
const translucentBg = 'bg-elevation-2-light/80 dark:bg-elevation-2-dark/80 backdrop-blur-sm';

const tintStyles: Record<CardTint, string> = {
  info: 'bg-info-light/10 dark:bg-info-dark/10 backdrop-blur-sm',
  success: 'bg-success-light/10 dark:bg-success-dark/10 backdrop-blur-sm',
  warning: 'bg-warning-light/10 dark:bg-warning-dark/10 backdrop-blur-sm',
  danger: 'bg-danger-light/10 dark:bg-danger-dark/10 backdrop-blur-sm',
};

const variantStyles: Record<NonNullable<CardProps['variant']>, string> = {
  flat: '',
  transparent: '',
  elevated: 'border border-border-light dark:border-border-dark shadow-sm',
  interactive: 'border border-border-light dark:border-border-dark shadow-sm hover:border-elevation-5-light dark:hover:border-elevation-5-dark cursor-pointer transition-colors duration-200 ease-out',
};

const paddingStyles: Record<NonNullable<CardProps['size']>, Record<NonNullable<CardProps['padding']>, string>> = {
  sm: {
    none: '',
    sm: 'px-2 py-1',
    md: 'px-2 py-1.5',
    lg: 'px-2.5 py-2',
  },
  md: {
    none: '',
    sm: 'p-1.5',
    md: 'p-3',
    lg: 'p-5',
  },
  lg: {
    none: '',
    sm: 'p-3',
    md: 'p-5',
    lg: 'p-7',
  },
};

const sizeStyles: Record<NonNullable<CardProps['size']>, string> = {
  sm: 'rounded-lg',
  md: 'rounded-lg',
  lg: 'rounded-xl',
};

export const Card = forwardRef<HTMLDivElement, CardProps>(function Card({
  variant = 'elevated',
  padding = 'md',
  size = 'lg',
  tint,
  defaultBackground = true,
  translucent = false,
  className = '',
  children,
  ...props
}, ref) {
  const bg = tint
    ? tintStyles[tint]
    : defaultBackground && variant !== 'transparent'
      ? (translucent ? translucentBg : solidBg)
      : '';

  const appliedVariant = tint ? variantStyles[variant].replace(/shadow-\S+/g, '') : variantStyles[variant];

  return (
    <div
      ref={ref}
      className={`relative ${sizeStyles[size]} ${bg} ${appliedVariant} ${paddingStyles[size][padding]} ${className}`}
      {...props}
    >
      {children}
    </div>
  );
});

export interface CardHeaderProps extends HTMLAttributes<HTMLDivElement> {
  children: ReactNode;
}

export function CardHeader({ className = '', children, ...props }: CardHeaderProps) {
  return (
    <div
      className={`flex items-center justify-between pb-4 border-b border-border-light dark:border-border-dark ${className}`}
      {...props}
    >
      {children}
    </div>
  );
}

export interface CardContentProps extends HTMLAttributes<HTMLDivElement> {
  children: ReactNode;
}

export function CardContent({ className = '', children, ...props }: CardContentProps) {
  return (
    <div className={`pt-4 ${className}`} {...props}>
      {children}
    </div>
  );
}

type CardSize = NonNullable<CardProps['size']>;
const CardListSizeContext = createContext<CardSize>('lg');

export interface CardListProps extends HTMLAttributes<HTMLDivElement> {
  variant?: CardProps['variant'];
  size?: CardSize;
  /** Vertical padding on the card container (space above first / below last item). */
  verticalPadding?: 'none' | 'sm' | 'md' | 'lg';
  children: ReactNode;
}

const cardListVerticalPaddingStyles: Record<NonNullable<CardListProps['verticalPadding']>, string> = {
  none: '',
  sm: '',
  md: 'py-0.5',
  lg: 'py-1.5',
};

const cardListDividerInset: Record<CardSize, number> = {
  sm: 2,
  md: 3,
  lg: 5,
};

export const CardList = forwardRef<HTMLDivElement, CardListProps>(function CardList({
  variant = 'elevated',
  size = 'lg',
  verticalPadding = 'md',
  className = '',
  children,
  ...props
}, ref) {
  const items = Children.toArray(children);

  return (
    <CardListSizeContext.Provider value={size}>
      <Card ref={ref} variant={variant} size={size} padding="none" className={`overflow-hidden ${cardListVerticalPaddingStyles[verticalPadding]} ${className}`} {...props}>
        {items.map((child, index) => (
          <Fragment key={(child as React.ReactElement).key ?? index}>
            {index > 0 && <Divider inset={cardListDividerInset[size]} />}
            {child}
          </Fragment>
        ))}
      </Card>
    </CardListSizeContext.Provider>
  );
});

const listItemSizeStyles: Record<CardSize, string> = {
  sm: 'mx-2 py-1.5',
  md: 'mx-3 py-2.5',
  lg: 'mx-5 py-4',
};

export interface CardListItemProps extends HTMLAttributes<HTMLDivElement> {
  children: ReactNode;
}

export function CardListItem({ className = '', children, ...props }: CardListItemProps) {
  const size = useContext(CardListSizeContext);
  return (
    <div className={`${listItemSizeStyles[size]} ${className}`} {...props}>
      {children}
    </div>
  );
}
