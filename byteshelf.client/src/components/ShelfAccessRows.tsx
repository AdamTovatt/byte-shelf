import { ButtonGroup } from '../ui';
import { NO_ACCESS } from '../lib/access';
import { runOrToast } from '../lib/utils';

/** One shelf's row: its name, what the caller has there, and the choices offered for it. */
export interface ShelfAccessRow<P extends string> {
  shelf: string;
  value: P | typeof NO_ACCESS;
  options: { value: P | typeof NO_ACCESS; label: string }[];
}

interface ShelfAccessRowsProps<P extends string> {
  rows: ShelfAccessRow<P>[];
  /** Gives the shelf with the permission, or changes it. */
  onSet: (shelf: string, permission: P) => Promise<unknown>;
  /** Takes the shelf away, for the "None" choice. */
  onRemove: (shelf: string) => Promise<unknown>;
  /** What the toast says when a change fails. */
  failure: (shelf: string) => string;
  ariaLabel: (shelf: string) => string;
}

/** A user's or a key's access on each shelf, each changed as it is picked. */
export function ShelfAccessRows<P extends string>({ rows, onSet, onRemove, failure, ariaLabel }: ShelfAccessRowsProps<P>) {
  function change(shelf: string, choice: P | typeof NO_ACCESS): Promise<void> {
    return runOrToast(() => (choice === NO_ACCESS ? onRemove(shelf) : onSet(shelf, choice)), failure(shelf));
  }

  return (
    <div className="divide-y divide-border">
      {rows.map((row) => (
        <div key={row.shelf} className="flex flex-wrap items-center justify-between gap-3 py-2">
          <span className="min-w-0 truncate text-body-md text-fg">{row.shelf}</span>
          <ButtonGroup size="sm" options={row.options} value={row.value} onChange={(choice) => void change(row.shelf, choice)} ariaLabel={ariaLabel(row.shelf)} />
        </div>
      ))}
    </div>
  );
}
