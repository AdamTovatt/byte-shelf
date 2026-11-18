/**
 * What a folder's search is narrowed to beyond its text: every filter the search options popover
 * (`components/SearchOptions.tsx`) offers, with its control there. None on is a plain text search.
 */
export interface SearchFilters {
  /** Only files whose contents another file found shares, in groups, most repeated bytes first. */
  duplicates: boolean;
  /** Only files that have no description yet, the ones left to describe. */
  undescribed: boolean;
}

export const NO_SEARCH_FILTERS: SearchFilters = { duplicates: false, undescribed: false };

/**
 * The criteria a folder's search sends, as the server's `FileCriteria` takes them: search and the
 * search for duplicates both take these whole.
 */
export interface SearchCriteria {
  /** Text in a file's name, description, or the title or camera read from its contents; blank for none. */
  text: string;
  /** False for only the files without a description; absent leaves it unchecked. */
  hasDescription?: boolean;
}

/** The criteria of the text typed and the filters on. */
export function searchCriteria(text: string, filters: SearchFilters): SearchCriteria {
  return { text: text.trim(), hasDescription: filters.undescribed ? false : undefined };
}

/** Whether the text typed or a filter on makes a search of the folder, rather than its listing. */
export function isSearch(text: string, filters: SearchFilters): boolean {
  return text.trim() !== '' || filters.duplicates || filters.undescribed;
}

/** Whether any filter is on, which tints the options' icon. */
export function anySearchFilter(filters: SearchFilters): boolean {
  return isSearch('', filters);
}
