import { useRef } from 'react';
import toast from 'react-hot-toast';
import { Button } from './Button';

export interface CopyableSecretProps {
  value: string;
}

/**
 * A secret shown once, such as a shelf key or a pairing code, with a button that copies it. Not unit
 * tested, like the other components: the client's unit tests run without a DOM and cover lib/.
 */
export function CopyableSecret({ value }: CopyableSecretProps) {
  const codeRef = useRef<HTMLElement>(null);

  function select() {
    if (codeRef.current) {
      window.getSelection()?.selectAllChildren(codeRef.current);
    }
  }

  function copy() {
    // Browsers offer the clipboard only on HTTPS and localhost: on plain HTTP the text is selected instead.
    if (!window.isSecureContext) {
      select();
      toast('Selected: press Ctrl+C or ⌘C to copy');
      return;
    }

    navigator.clipboard.writeText(value).then(
      () => toast.success('Copied'),
      () => {
        select();
        toast.error('Could not copy. It is selected: press Ctrl+C or ⌘C');
      },
    );
  }

  return (
    <>
      <code ref={codeRef} className="block break-all rounded-lg bg-elevation-3-light dark:bg-elevation-3-dark p-3 text-body-sm text-text-1-light dark:text-text-1-dark">{value}</code>
      <Button variant="secondary" onClick={copy}>Copy</Button>
    </>
  );
}
