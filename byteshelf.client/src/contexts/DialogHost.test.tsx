// @vitest-environment happy-dom
import { StrictMode, act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DialogHost, useShowDialog, type ShowDialog, type ShownDialog } from './DialogHost';
import { button, press } from '../test/dom';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('dialogs shown by code', () => {
  let root: Root;
  let show: ShowDialog;

  function Page() {
    const showHere = useShowDialog();
    useEffect(() => {
      show = showHere;
    });
    return null;
  }

  function render(page: boolean): void {
    act(() => root.render(<StrictMode><DialogHost>{page && <Page />}</DialogHost></StrictMode>));
  }

  /** Shows a dialog that is a button, closing it as it is pressed. */
  function showButton(name: string): ShownDialog {
    let shown!: ShownDialog;
    act(() => {
      shown = show((close) => <button onClick={close}>{name}</button>);
    });
    return shown;
  }

  function names(): string[] {
    return Array.from(document.querySelectorAll('button'), (each) => each.textContent!);
  }

  beforeEach(() => {
    root = createRoot(document.body.appendChild(document.createElement('div')));
    render(true);
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.restoreAllMocks();
  });

  it('are there in the order they were shown, each until it closes itself', async () => {
    const first = showButton('First');
    const second = showButton('Second');
    expect(names()).toEqual(['First', 'Second']);

    await press('First');
    await first.closed;
    expect(names()).toEqual(['Second']);

    await press('Second');
    await second.closed;
    expect(names()).toEqual([]);
  });

  it('close as what showed them says so', async () => {
    const { close, closed } = showButton('First');

    act(close);

    await closed;
    expect(names()).toEqual([]);
  });

  it('stay the same ones as another one closes', async () => {
    showButton('First');
    showButton('Second');
    const second = button('Second');

    await press('First');

    expect(button('Second')).toBe(second);
  });

  it('leave the page and the others there as one throws as it closes', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    let fails = false;
    function Failing({ close }: { close: () => void }) {
      useEffect(() => () => {
        if (fails) throw new Error('It failed as it went.');
      }, []);
      return <button onClick={close}>First</button>;
    }
    act(() => void show((close) => <Failing close={close} />));
    const second = showButton('Second');
    fails = true;

    await press('First');
    expect(names()).toEqual(['Second']);

    await press('Second');
    await second.closed;
    expect(names()).toEqual([]);
  });

  it('leave the others there as one throws as it closes, though they would too', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    // Without StrictMode, where they would as they are drawn too.
    act(() => root.render(<DialogHost><Page /></DialogHost>));
    let drawn = 0;
    function Failing({ name, close }: { name: string; close: () => void }) {
      useEffect(() => {
        drawn++;
        return () => {
          // Not for ever, which would be tried for as long.
          if (drawn < 50) throw new Error('It failed as it went.');
        };
      }, []);
      return <button onClick={close}>{name}</button>;
    }
    act(() => void show((close) => <Failing name="First" close={close} />));
    act(() => void show((close) => <Failing name="Second" close={close} />));

    await press('First');

    expect(names()).toEqual(['Second']);
    expect(drawn).toBe(3);
    drawn = 50;
  });

  it('close as the page that showed them is left, which shows no more', async () => {
    const { closed } = showButton('First');

    render(false);

    await closed;
    expect(names()).toEqual([]);
    await showButton('Late').closed;
    expect(names()).toEqual([]);
  });
});
