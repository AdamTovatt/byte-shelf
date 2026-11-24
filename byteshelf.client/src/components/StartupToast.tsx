import { useEffect } from 'react';
import toast from 'react-hot-toast';
import { takeStartupToast } from '../lib/startupToast';

/** Shows the toast the page before this one left for it, such as why it started anew. */
export function StartupToast() {
  useEffect(() => {
    const text = takeStartupToast();
    if (text) toast.success(text);
  }, []);

  return null;
}
