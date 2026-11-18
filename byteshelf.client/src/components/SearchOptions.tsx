import { DropdownOptionRow, Icon, IconButton, Popover, iconMap } from '../ui';
import { anySearchFilter, type SearchFilters } from '../lib/searchFilters';

interface SearchOptionsProps {
  filters: SearchFilters;
  onChange: (filters: SearchFilters) => void;
}

/**
 * The search field's options: a filter icon at the field's end, tinted while a filter is on, opening
 * a popover with a control for every filter in {@link SearchFilters}. It stays open as they change.
 */
export function SearchOptions({ filters, onChange }: SearchOptionsProps) {
  return (
    <Popover
      align="right"
      position="below"
      trigger={(
        <IconButton
          variant="ghost"
          size="sm"
          icon={iconMap.filter}
          iconClassName={anySearchFilter(filters) ? 'text-brand-11' : undefined}
          aria-label="Search options"
        />
      )}
    >
      <div role="group" aria-label="Search options" className="min-w-52 p-1">
        <SearchToggle label="Duplicates only" on={filters.duplicates} onToggle={() => onChange({ ...filters, duplicates: !filters.duplicates })} />
      </div>
    </Popover>
  );
}

interface SearchToggleProps {
  label: string;
  on: boolean;
  onToggle: () => void;
}

/** A filter that is on or off: the menu row, with a tick while on, as a checked `DropdownItem` draws it. */
function SearchToggle({ label, on, onToggle }: SearchToggleProps) {
  return (
    <DropdownOptionRow
      label={label}
      aria-pressed={on}
      onClick={onToggle}
      leading={on ? <Icon icon={iconMap.success} size={14} /> : <span className="w-3.5" />}
    />
  );
}
