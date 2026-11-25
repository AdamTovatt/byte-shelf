import { describe, expect, it, vi } from 'vitest';
import type { Upload } from '../services/uploadQueue';
import { uploadStore, type HeldState } from './uploadStore';

function upload(id: number, folderPath = '/a'): Upload {
  return { id, shelf: 'docs', folderPath, file: new File(['new'], `${id}.txt`), name: `${id}.txt`, path: `${folderPath}/${id}.txt` };
}

const underway = { replacing: false, cancel: () => undefined };

function sending(sentBytes: number): HeldState {
  return { status: 'uploading', sentBytes, totalBytes: 10, ...underway };
}

describe('the uploads shown', () => {
  it('keep a folder\'s list as its uploads are sent, and change it as one is added, replaces or goes', () => {
    const store = uploadStore(() => undefined);
    const lists = [store.inFolder('docs', '/a')];
    const listNow = () => lists.push(store.inFolder('docs', '/a'));

    store.report(upload(1), { status: 'waiting', ...underway });
    listNow();
    store.report(upload(1), sending(0));
    store.report(upload(1), sending(5));
    listNow();
    store.report(upload(1), { status: 'taken', replace: () => undefined });
    store.report(upload(1), { status: 'waiting', ...underway, replacing: true });
    listNow();
    store.remove(1);
    listNow();

    expect(lists.map((list) => list.map(({ replacing }) => replacing))).toEqual([[], [false], [false], [true], []]);
    expect(lists[2]).toBe(lists[1]);
  });

  it('tell each upload\'s state as it moves on, and what listens of each change', () => {
    const store = uploadStore(() => undefined);
    const told = vi.fn();
    store.subscribe(told);

    store.report(upload(1), sending(5));

    expect(store.stateOf(1)).toEqual(sending(5));
    expect(told).toHaveBeenCalled();
  });

  it('keep each folder\'s apart, and forget one cancelled', () => {
    const store = uploadStore(() => undefined);

    store.report(upload(1), sending(0));
    store.report(upload(2, '/b'), sending(0));
    store.report(upload(3), sending(0));
    store.report(upload(3), { status: 'cancelled' });

    expect(store.inFolder('docs', '/a').map(({ upload: { id } }) => id)).toEqual([1]);
    expect(store.inFolder('docs', '/b').map(({ upload: { id } }) => id)).toEqual([2]);
    expect(store.inFolder('other', '/a')).toEqual([]);
    expect(store.stateOf(3)).toBeNull();
  });

  it('tell of a name taken or a failure where nothing shows its folder, and only then', () => {
    const missed = vi.fn();
    const store = uploadStore(missed);
    const taken: HeldState = { status: 'taken', replace: () => undefined };
    const failed: HeldState = { status: 'failed', error: new Error('Full.') };

    const hide = store.show('docs', '/a');
    const hideAgain = store.show('docs', '/a');
    store.report(upload(1), taken);
    hide();
    store.report(upload(2), failed);
    hideAgain();
    store.report(upload(3), sending(0));
    store.report(upload(3), failed);
    store.report(upload(4, '/b'), taken);

    expect(missed.mock.calls.map(([{ id }, { status }]) => [id, status])).toEqual([[3, 'failed'], [4, 'taken']]);
  });
});
