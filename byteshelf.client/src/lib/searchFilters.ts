/**
 * What a folder's search is narrowed to beyond its text: every filter the search options popover
 * (`components/SearchOptions.tsx`) offers, with its control there. None on is a plain text search.
 */
export interface SearchFilters {
  /** Only files whose contents another file found shares, in groups, most repeated bytes first. */
  duplicates: boolean;
}

export const NO_SEARCH_FILTERS: SearchFilters = { duplicates: false };

/** Whether any filter is on, which makes a search of the folder even with no text. */
export function anySearchFilter(filters: SearchFilters): boolean {
  return filters.duplicates;
}
