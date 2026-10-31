export interface SectionTitleProps {
  children: string;
}

/** The heading of one section of a page, such as Keys in a shelf's settings. */
export function SectionTitle({ children }: SectionTitleProps) {
  return <h2 className="text-body-lg font-medium text-text-3-light dark:text-text-3-dark">{children}</h2>;
}
