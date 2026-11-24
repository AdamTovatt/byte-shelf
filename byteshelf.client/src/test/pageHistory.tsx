import { act } from 'react';
import { useLocation, useNavigate, type NavigateFunction } from 'react-router-dom';

/** The history of a rendered page, followed and gone through once `Follow` is drawn in its router. */
export function pageHistory() {
  let navigate: NavigateFunction;
  const page = {
    /** Where the page is: its path and query. */
    url: '',
    back: () => act(() => void navigate(-1)),
    forward: () => act(() => void navigate(1)),
    to: (url: string) => act(() => void navigate(url)),
    Follow: () => {
      const { pathname, search } = useLocation();
      page.url = pathname + search;
      navigate = useNavigate();
      return null;
    },
  };
  return page;
}
