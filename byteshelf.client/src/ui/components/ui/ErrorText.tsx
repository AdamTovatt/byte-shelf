import type { ReactNode } from 'react';
import { hasContent } from '../../lib/hasContent';

export interface ErrorTextProps {
  /** For the field it describes to point its `aria-describedby` at. Set only while there is a message. */
  id?: string;
  /** `alert` for a message that appears on its own and must be announced (a form-level error). */
  role?: 'alert';
  testId?: string;
  className?: string;
  /** The refusal. Nothing (`undefined`, `null`, `false` or `''`) draws no message. */
  children?: ReactNode;
}

const ERROR_TEXT_CLASS = 'text-body-sm text-danger-11';

/**
 * Finds every line an `ErrorText` draws by the attribute it carries — so a test
 * reads "the error line" by what it is rather than by the ink or layout classes it happens to wear.
 * Exported from this module, not the package index, for this package's own tests.
 */
export const ERROR_LINE_SELECTOR = '[data-error-line]';

/**
 * The one way a refusal is worded on screen — under a field (`Input`, `Textarea`, `Select`) and as a
 * form's own line (`FormError`) alike — so the two read as the same kind of statement and there is a
 * single place their ink and size are spelled.
 *
 * With no message it draws nothing, so no room is kept: a refusal appears under its field and moves
 * what sits below, the way shadcn and Material UI forms do (#2455). A `role="alert"` line is mounted
 * with its words in it, which is how it is announced.
 */
export function ErrorText({ id, role, testId, className = '', children }: ErrorTextProps) {
  if (!hasContent(children, { ignoreEmptyString: true })) return null;
  return (
    <p data-error-line="" id={id} role={role} data-testid={testId} className={`${ERROR_TEXT_CLASS} ${className}`}>
      {children}
    </p>
  );
}
