import { Spinner } from '../../ui';

/** Something the preview shows is loading. */
export function PreviewSpinner() {
  return (
    <div className="flex min-h-0 flex-1 items-center justify-center">
      <Spinner size="lg" />
    </div>
  );
}
