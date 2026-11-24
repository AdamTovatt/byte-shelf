// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { giveFiles, untilFound } from '../test/dom';
import { useFilePicker } from './useFilePicker';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('the file picker', () => {
  let root: Root;
  let picker: ReturnType<typeof useFilePicker>;

  function Page() {
    picker = useFilePicker();
    return picker.input;
  }

  beforeEach(async () => {
    root = createRoot(document.body.appendChild(document.createElement('div')));
    await act(async () => root.render(<Page />));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
  });

  it('gives the files picked to what opened it last, and forgets them, so that they can be picked again', async () => {
    const input = await untilFound<HTMLInputElement>('input[type="file"]');
    const files = [new File(['a'], 'a.txt')];
    const before = vi.fn();
    const given = vi.fn();
    const reset = vi.spyOn(input, 'value', 'set').mockImplementation(() => undefined);

    picker.open(before);
    picker.open(given);
    await giveFiles(input, files);

    expect(given).toHaveBeenCalledExactlyOnceWith(files);
    expect(before).not.toHaveBeenCalled();
    expect(reset).toHaveBeenCalledWith('');
  });

  it('gives nothing as none are picked', async () => {
    const given = vi.fn();

    picker.open(given);
    await giveFiles(await untilFound<HTMLInputElement>('input[type="file"]'), []);

    expect(given).not.toHaveBeenCalled();
  });
});
