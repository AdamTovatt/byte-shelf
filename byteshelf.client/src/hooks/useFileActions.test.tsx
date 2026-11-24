// @vitest-environment happy-dom
import { act, lazy, useState, type ComponentType, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import toast from 'react-hot-toast';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useFileActions, type FileActions } from './useFileActions';
import type { PluginHostState } from '../lib/plugins/pluginHost';
import type { FileAction, FileActionContext } from '../lib/plugins/fileAction';
import type { ContentSource } from '../services/contentService';
import type { OpenableFile } from '../services/fileReading';
import type { ShelfFile } from '../services/shelfService';
import { button, press } from '../test/dom';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const host = vi.hoisted(() => ({ state: undefined as unknown }));
vi.mock('./usePluginHost', () => ({ usePluginHost: () => host.state }));

const file = { id: 'f', name: 'secret.txt', encrypted: true } as ShelfFile;

describe('running a file action', () => {
  let root: Root;
  let actions: FileActions;
  let dialogs: ReactNode;
  let target: OpenableFile;
  let errors: unknown[];

  function Page() {
    ({ actions, dialogs } = useFileActions('docs'));
    const [presses, setPresses] = useState(0);
    return <><button onClick={() => setPresses(presses + 1)}>Pressed {presses}</button>{dialogs}</>;
  }

  function action(run: FileAction['run']): FileAction {
    return { id: 'act', label: 'Act', showsFor: () => true, run };
  }

  /** Runs `run` as an action, answering once it is done and its dialogs are closed. */
  async function runs(run: FileAction['run']): Promise<{ done: Promise<void> }> {
    let done = Promise.resolve();
    await act(async () => {
      done = actions.run(action(run), target);
    });
    return { done };
  }

  function dialogTitles(): (string | null)[] {
    return Array.from(document.querySelectorAll('[role="dialog"] h2'), (title) => title.textContent);
  }

  beforeEach(() => {
    host.state = { plugins: [{ id: 'a', viewers: [], fileActions: [action(() => undefined)] }], failures: new Map() } satisfies PluginHostState;
    target = { file, source: {} as ContentSource, readWhole: async () => new Blob(['plain']) };
    errors = [];
    vi.spyOn(toast, 'error').mockImplementation((message) => {
      errors.push(message);
      return '';
    });
    root = createRoot(document.body.appendChild(document.createElement('div')));
    act(() => root.render(<Page />));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.restoreAllMocks();
  });

  it('gives it the shelf, the file and its contents, decrypted', async () => {
    let given: FileActionContext | undefined;
    let text = '';

    await (await runs(async (context) => {
      given = context;
      text = await (await context.content.blob()).text();
    })).done;

    expect(actions.actionsFor(file).map(({ action: { id } }) => id)).toEqual(['act']);
    expect([given!.shelf, given!.file, text]).toEqual(['docs', file, 'plain']);
    expect(errors).toEqual([]);
  });

  it.each([
    ['throws', () => { throw new Error('It threw.'); }],
    ['rejects', async () => { throw new Error('It threw.'); }],
  ])('says why one that %s failed', async (_, run) => {
    await (await runs(run)).done;

    expect(errors).toEqual(['It threw.']);
  });

  it('names it when what it threw says nothing', async () => {
    await (await runs(async () => Promise.reject(null as unknown as Error))).done;

    expect(errors).toEqual(['Act failed']);
  });

  it('says why one was stopped by the browser, which is no giving up', async () => {
    await (await runs(() => { throw new DOMException('It may not.', 'SecurityError'); })).done;

    expect(errors).toEqual(['It may not.']);
  });

  it('says nothing of one whose password was not given', async () => {
    target = { ...target, readWhole: async () => null };

    await (await runs(async ({ content }) => void await content.blob())).done;

    expect(errors).toEqual([]);
  });

  it('shows a dialog until it closes itself, and keeps the contents readable until then', async () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:made');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const { done } = await runs(async ({ content, showDialog }) => {
      const url = await content.url();
      showDialog((close) => <button onClick={close}>Close {url}</button>);
    });
    await vi.waitFor(async () => {
      await act(async () => undefined);
      expect(button('Close blob:made')).not.toBeNull();
    });
    expect(revoke).not.toHaveBeenCalled();

    await press('Close blob:made');
    await done;

    expect(dialogs).toBeNull();
    await vi.waitFor(() => expect(revoke).toHaveBeenCalledWith('blob:made'));
  });

  it('keeps the contents readable while a dialog is open that another showed', async () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:made');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const { done } = await runs(async ({ content, showDialog }) => {
      await content.url();
      showDialog((close) => (
        <button onClick={() => { close(); showDialog((closeNext) => <button onClick={closeNext}>Second</button>); }}>First</button>
      ));
    });
    await vi.waitFor(() => act(async () => expect(button('First')).not.toBeNull()));

    await press('First');
    await act(() => new Promise((resolve) => { setTimeout(resolve, 20); }));
    expect(revoke).not.toHaveBeenCalled();

    await press('Second');
    await done;
    await vi.waitFor(() => expect(revoke).toHaveBeenCalledWith('blob:made'));
  });

  it('shows a dialog that still loads as such, closable, and the page goes on meanwhile', async () => {
    let bring: (module: { default: ComponentType }) => void = () => undefined;
    const Late = lazy(() => new Promise<{ default: ComponentType }>((resolve) => { bring = resolve; }));
    await runs(({ showDialog }) => showDialog(() => <Late />));
    expect(dialogTitles()).toEqual(['Act']);
    expect(button('Close')).not.toBeNull();

    await press('Pressed 0');
    expect(button('Pressed 1')).not.toBeNull();

    await act(async () => bring({ default: () => <p>drawn late</p> }));
    expect(document.body.textContent).toContain('drawn late');
    expect(dialogTitles()).toEqual([]);
  });

  function Broken(): never {
    throw new Error('It threw.');
  }

  it.each<[string, () => ReactNode]>([
    ['cannot be made', () => { throw new Error('It threw.'); }],
    ['cannot be drawn', () => <Broken />],
  ])('says in its place that a dialog that %s failed, under the action\'s name, and closes it', async (_, dialog) => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const { done } = await runs(({ showDialog }) => showDialog(dialog));
    expect(dialogTitles()).toEqual(['Act']);
    expect(document.body.textContent).toContain('The dialog failed.');

    await press('Close');
    await done;

    expect(dialogTitles()).toEqual([]);
  });

  it('leaves another dialog be as one fails', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await runs(({ showDialog }) => {
      showDialog(() => <Broken />);
      showDialog((close) => <button onClick={close}>The other</button>);
    });

    expect(button('The other')).not.toBeNull();
  });

  it('lets go of the contents as the page is left', async () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:made');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    await runs(async ({ content, showDialog }) => {
      await content.url();
      showDialog(() => <p>open</p>);
    });
    await vi.waitFor(async () => {
      await act(async () => undefined);
      expect(document.body.textContent).toContain('open');
    });

    act(() => root.unmount());
    root = createRoot(document.body.appendChild(document.createElement('div')));

    await vi.waitFor(() => expect(revoke).toHaveBeenCalledWith('blob:made'));
  });
});
