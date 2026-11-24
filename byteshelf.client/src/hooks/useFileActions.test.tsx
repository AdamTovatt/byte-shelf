// @vitest-environment happy-dom
import { act, lazy, useState, type ComponentType, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import toast from 'react-hot-toast';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DialogHost } from '../contexts/DialogHost';
import { usePluginId } from '../contexts/PluginIdentity';
import { useFileActions, type FileActions } from './useFileActions';
import { usePasswordPrompt, type AskPassword } from './usePasswordPrompt';
import { Modal } from '../ui';
import type { PluginHostState } from '../lib/plugins/pluginHost';
import type { FileAction, FileActionContext } from '../lib/plugins/fileAction';
import type { ContentSource } from '../services/contentService';
import type { OpenableFile } from '../services/fileReading';
import type { ShelfFile } from '../services/shelfService';
import { button, dialogTitles, field, press } from '../test/dom';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const host = vi.hoisted(() => ({ state: undefined as unknown }));
vi.mock('./usePluginHost', () => ({ usePluginHost: () => host.state }));

const file = { id: 'f', name: 'secret.txt', encrypted: true } as ShelfFile;

describe('running a file action', () => {
  let root: Root;
  let actions: FileActions;
  let ask: AskPassword;
  let target: OpenableFile;
  let errors: unknown[];

  function Page() {
    actions = useFileActions('docs');
    ask = usePasswordPrompt();
    const [presses, setPresses] = useState(0);
    return <button onClick={() => setPresses(presses + 1)}>Pressed {presses}</button>;
  }

  function action(run: FileAction['run']): FileAction {
    return { id: 'act', label: 'Act', showsFor: () => true, run };
  }

  /** Leaves the page, but not what holds its dialogs. */
  function leave(): Promise<void> {
    return act(async () => root.render(<DialogHost>{null}</DialogHost>));
  }

  /** Runs `run` as an action, answering once it is done and its dialogs are closed. */
  async function runs(run: FileAction['run']): Promise<{ done: Promise<void> }> {
    let done = Promise.resolve();
    await act(async () => {
      done = actions.run({ pluginId: 'a', key: 'a act', action: action(run) }, target);
    });
    return { done };
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
    act(() => root.render(<DialogHost><Page /></DialogHost>));
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

    expect(button('Close blob:made')).toBeNull();
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
    expect(button('Second')).not.toBeNull();
    // All that follows the first closing is done in promises, which are all run by the next timer.
    await act(() => new Promise((resolve) => { setTimeout(resolve); }));
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

  it('keeps a dialog that loads under the password prompt asked for meanwhile, which keeps the focus', async () => {
    let bring: (module: { default: ComponentType }) => void = () => undefined;
    const Late = lazy(() => new Promise<{ default: ComponentType }>((resolve) => { bring = resolve; }));
    await runs(({ showDialog }) => showDialog(() => <Late />));
    await act(async () => {
      void ask({ kind: 'unlock', name: 'a', verify: async () => true });
    });
    const prompt = document.activeElement;
    expect(prompt).toBe(field('Password'));

    await act(async () => bring({ default: () => <Modal isOpen title="Late" onClose={() => undefined}><Modal isOpen title="Inside" onClose={() => undefined}>It</Modal></Modal> }));

    expect(dialogTitles()).toEqual(['Late', 'Inside', 'Unlock a']);
    expect(document.activeElement).toBe(prompt);
    await press('Cancel');
    expect(dialogTitles()).toEqual(['Late', 'Inside']);
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Inside');
  });

  function Owner() {
    return <p>of the plugin {usePluginId()}</p>;
  }

  it('shows its dialogs as the plugin\'s: what they draw late, and one shown by another, too', async () => {
    const Late = lazy(async () => ({ default: Owner }));
    await runs(({ showDialog }) => showDialog((close) => (
      <><Late /><button onClick={() => { close(); showDialog(() => <Modal isOpen title="Next" onClose={() => undefined}><Owner /></Modal>); }}>Next</button></>
    )));
    await vi.waitFor(() => act(async () => expect(document.body.textContent).toContain('of the plugin a')));

    await press('Next');

    expect(dialogTitles()).toEqual(['Next']);
    expect(document.body.textContent).toContain('of the plugin a');
  });

  it('keeps apart whose the dialogs of two plugins are', async () => {
    await act(async () => {
      for (const pluginId of ['a', 'b']) void actions.run({ pluginId, key: pluginId, action: action(({ showDialog }) => showDialog(() => <Owner />)) }, target);
    });

    expect(document.body.textContent).toMatch(/of the plugin a.*of the plugin b/);
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

  it('lets go of the contents of an action still at it as the page is left', async () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:made');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    let read = false;
    await runs(async ({ content }) => {
      await content.url();
      read = true;
      await new Promise(() => undefined);
    });
    await vi.waitFor(() => expect(read).toBe(true));

    await leave();

    await vi.waitFor(() => expect(revoke).toHaveBeenCalledWith('blob:made'));
  });

  it('lets go of the contents, and closes its dialog, as the page is left', async () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:made');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const { done } = await runs(async ({ content, showDialog }) => {
      await content.url();
      showDialog(() => <p>open</p>);
    });
    await vi.waitFor(async () => {
      await act(async () => undefined);
      expect(document.body.textContent).toContain('open');
    });

    await leave();

    expect(document.body.textContent).not.toContain('open');
    await done;
    await vi.waitFor(() => expect(revoke).toHaveBeenCalledWith('blob:made'));
  });
});
