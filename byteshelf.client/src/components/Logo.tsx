interface LogoProps {
  className?: string;
  /** Empty by default — the logo is decorative next to a text label; set it when it stands alone. */
  alt?: string;
}

/** The ByteShelf app icon. Centralizes the asset path so callers only choose size and shape. */
export function Logo({ className = '', alt = '' }: LogoProps) {
  return <img src="/icon-192.png" alt={alt} className={className} />;
}
