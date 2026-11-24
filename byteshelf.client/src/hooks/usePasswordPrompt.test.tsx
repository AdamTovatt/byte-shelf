// @vitest-environment happy-dom
import { act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { usePasswordPrompt, type AskPassword } from './usePasswordPrompt';
import { DialogHost } from '../contexts/DialogHost';
import { dialogTitles, submitPassword } from '../test/dom';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('the password prompt', () => {
  let root: Root;
  let ask: AskPassword;

  function Page() {
    const askHere = usePasswordPrompt();
    useEffect(() => {
      ask = askHere;
    });
    return null;
  }

  async function asks(name: string, signal?: AbortSignal): Promise<{ answer: Promise<string | null> }> {
    let answer!: Promise<string | null>;
    await act(async () => {
      answer = ask({ kind: 'unlock', name, verify: async () => true }, signal);
    });
    return { answer };
  }

  beforeEach(async () => {
    root = createRoot(document.body.appendChild(document.createElement('div')));
    await act(async () => root.render(<DialogHost><Page /></DialogHost>));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
  });

  it('answers the password given', async () => {
    const { answer } = await asks('a');

    await submitPassword('pw');

    expect(await answer).toBe('pw');
    expect(dialogTitles()).toEqual([]);
  });

  it('takes the place of the one before, which is answered with none', async () => {
    const { answer: first } = await asks('a');
    const { answer: second } = await asks('b');

    expect(await first).toBeNull();
    expect(dialogTitles()).toEqual(['Unlock b']);
    await submitPassword('pw');
    expect(await second).toBe('pw');
  });

  it('is taken back as its signal aborts', async () => {
    const aborting = new AbortController();
    const { answer } = await asks('a', aborting.signal);

    act(() => aborting.abort());

    expect(await answer).toBeNull();
    expect(dialogTitles()).toEqual([]);
  });

  it('stays as the signal of the one before aborts', async () => {
    const aborting = new AbortController();
    await asks('a', aborting.signal);
    await asks('b');

    act(() => aborting.abort());

    expect(dialogTitles()).toEqual(['Unlock b']);
  });

  it('does not show for a signal that has aborted', async () => {
    const { answer } = await asks('a', AbortSignal.abort());

    expect(await answer).toBeNull();
    expect(dialogTitles()).toEqual([]);
  });

  it('is answered with none as it is cancelled, also when its password is found right after that', async () => {
    let found: (isRight: boolean) => void = () => undefined;
    let answer!: Promise<string | null>;
    await act(async () => {
      answer = ask({ kind: 'unlock', name: 'a', verify: () => new Promise((resolve) => { found = resolve; }) });
    });
    await submitPassword('pw');
    const { answer: second } = await asks('b');

    await act(async () => found(true));

    expect(await answer).toBeNull();
    expect(dialogTitles()).toEqual(['Unlock b']);
    await submitPassword('pw');
    expect(await second).toBe('pw');
  });

  it('is answered with none as the page that asks is left', async () => {
    const { answer } = await asks('a');

    await act(async () => root.render(<DialogHost>{null}</DialogHost>));

    expect(await answer).toBeNull();
    expect(dialogTitles()).toEqual([]);
  });
});
