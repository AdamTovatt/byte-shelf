// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import toast, { Toaster } from 'react-hot-toast';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { press } from '../test/dom';
import type { Upload, UploadState } from '../services/uploadQueue';
import { reportUpload } from './uploadToasts';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const UPLOAD: Upload = { id: 1, file: new File(['new'], 'a.txt'), path: '/a/a.txt' };
const TAKEN = "A file already exists at '/a/a.txt'.";

describe('an upload\'s toast', () => {
  let root: Root;
  let container: HTMLElement;

  beforeEach(() => {
    vi.useFakeTimers();
    container = document.body.appendChild(document.createElement('div'));
    root = createRoot(container);
    act(() => root.render(<Toaster />));
  });

  afterEach(() => {
    act(() => {
      toast.remove();
      root.unmount();
    });
    document.body.innerHTML = '';
    vi.useRealTimers();
  });

  function tell(state: UploadState): void {
    act(() => reportUpload(UPLOAD, state));
  }

  /** Lets time pass a step at a time, as a toast's next timer is set once it is drawn after the last. */
  function pass(milliseconds: number): void {
    for (let passed = 0; passed < milliseconds; passed += 500) act(() => void vi.advanceTimersByTime(500));
  }

  it('is gone once it is cancelled', () => {
    tell({ status: 'uploading', sentBytes: 0 });
    expect(container.textContent).toContain('0 B of 3 B');

    tell({ status: 'cancelled' });
    pass(2_000);

    expect(container.textContent).toBe('');
  });

  it('offers a taken name until that is answered, and replaces it', async () => {
    const replace = vi.fn();
    tell({ status: 'taken', replace });
    pass(60_000);
    expect(container.textContent).toContain(TAKEN);
    expect(container.textContent).not.toContain('Could not replace it');

    await press('Replace');
    pass(2_000);

    expect(replace).toHaveBeenCalledOnce();
    expect(container.textContent).toBe('');
  });

  it('is gone, with nothing replaced, as it is dismissed', async () => {
    const replace = vi.fn();
    tell({ status: 'taken', replace });

    await press('Dismiss');
    pass(2_000);

    expect(replace).not.toHaveBeenCalled();
    expect(container.textContent).toBe('');
  });

  it('says why a taken name could not be replaced, and stays', () => {
    tell({ status: 'taken', failure: new Error('It is down.'), replace: () => undefined });
    pass(60_000);

    expect(container.textContent).toContain(`${TAKEN}Could not replace it: It is down.`);
  });

  it('is gone a moment after it is done, also after its name was taken', () => {
    tell({ status: 'taken', replace: () => undefined });
    tell({ status: 'uploading', sentBytes: 0 });
    tell({ status: 'done' });
    expect(container.textContent).toContain('Uploaded');

    pass(4_500);

    expect(container.textContent).toBe('');
  });

  it('says why it failed, for a while', () => {
    tell({ status: 'failed', error: new Error('The shelf is full.') });
    pass(7_000);
    expect(container.textContent).toContain('The shelf is full.');

    pass(3_000);

    expect(container.textContent).toBe('');
  });
});
