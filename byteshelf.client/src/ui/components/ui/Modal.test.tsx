// @vitest-environment happy-dom
import { StrictMode, act, useLayoutEffect, useState, type ReactNode } from 'react';
import { flushSync } from 'react-dom';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Modal } from './Modal';
import { OverlaySlotContext, useIsOverlayOpen, type OverlaySlot } from '../../hooks/useOverlay';
import { dialogTitles, dimmingTitles } from '../../../test/dom';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('dialogs over one another', () => {
  let root: Root;
  let closed: string[];

  interface Open {
    first?: boolean;
    inside?: boolean;
    second?: boolean;
  }

  /** The first dialog, which another may be inside, and a second beside it. */
  function show({ first = false, inside = false, second = false }: Open): Promise<void> {
    return act(async () => root.render(
      <>
        <Modal isOpen={second} onClose={() => closed.push('second')} title="Second">Second</Modal>
        <Modal isOpen={first} onClose={() => closed.push('first')} title="First">
          <Modal isOpen={inside} onClose={() => closed.push('inside')} title="Inside">Inside</Modal>
        </Modal>
      </>,
    ));
  }

  function press(key: string): void {
    act(() => void document.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true })));
  }

  beforeEach(() => {
    closed = [];
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
  });

  it.each<[string, Open[], string]>([
    ['the one inside, that opened with the one around it', [{ first: true, inside: true }], 'inside'],
    ['the one inside, that opened after the one around it', [{ first: true }, { first: true, inside: true }], 'inside'],
    ['the one beside it that opened last', [{ first: true }, { first: true, second: true }], 'second'],
    ['the one beside it that opened last, though it comes first', [{ second: true }, { first: true, second: true }], 'first'],
    ['the one beside it that opened last, over the one inside', [{ first: true, inside: true }, { first: true, inside: true, second: true }], 'second'],
    ['the one left', [{ first: true }, { first: true, second: true }, { first: true }], 'first'],
  ])('have on top, with Escape, %s', async (_, steps, expected) => {
    for (const step of steps) await show(step);

    expect(dialogTitles().at(-1)?.toLowerCase()).toBe(expected);
    press('Escape');
    expect(closed).toEqual([expected]);
  });

  it('give Escape to one alone, also when that closes it at once', async () => {
    await act(async () => root.render(<StrictMode><Closing /></StrictMode>));

    press('Escape');

    expect(dialogTitles()).toEqual(['First']);
  });

  /** Two dialogs, one inside the other, that close at once as they are told to, rather than once React gets to it. */
  function Closing() {
    const [open, setOpen] = useState(2);
    const close = () => flushSync(() => setOpen((count) => count - 1));
    return (
      <Modal isOpen={open > 0} onClose={close} title="First">
        <Modal isOpen={open > 1} onClose={close} title="Second">Second</Modal>
      </Modal>
    );
  }

  it('dim the page once, under the lowest', async () => {
    await show({ first: true, inside: true });
    expect(dimmingTitles()).toEqual(['First']);

    await show({ first: true, inside: true, second: true });
    expect(dimmingTitles()).toEqual(['First']);

    await show({ second: true });
    expect(dimmingTitles()).toEqual(['Second']);
  });

  it('are drawn over one another, and dim the page once, as they are first painted', async () => {
    const painted = new Promise((resolve) => {
      function Page() {
        // After what React draws again for layout effects, and before any effect that waits for the paint.
        useLayoutEffect(() => queueMicrotask(() => resolve([dialogTitles(), dimmingTitles()])), []);
        return <Modal isOpen onClose={() => {}} title="First"><Modal isOpen onClose={() => {}} title="Inside">Inside</Modal></Modal>;
      }
      // As the app is drawn: `act` runs every effect at once.
      vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', false);
      root.render(<Page />);
    });

    expect(await painted).toEqual([['First', 'Inside'], ['First']]);
    vi.unstubAllGlobals();
  });

  it('keep the place of the one before in their slot, under what has opened since', async () => {
    const slot: OverlaySlot = { openCount: 0, order: null };
    const render = (title: string | null, others: string[], inside?: string) => act(async () => root.render(
      <>
        <OverlaySlotContext.Provider value={slot}>
          {title && (
            <Modal key={title} isOpen onClose={() => {}} title={title}>
              {inside && <Modal isOpen onClose={() => {}} title={inside}>{inside}</Modal>}
            </Modal>
          )}
        </OverlaySlotContext.Provider>
        {others.map((other) => <Modal key={other} isOpen onClose={() => {}} title={other}>{other}</Modal>)}
      </>,
    ));
    await render(null, ['Under']);
    await render('Loading', ['Under']);
    await render('Loading', ['Under', 'Prompt']);

    await render('Loaded', ['Under', 'Prompt'], 'Inside');
    expect(dialogTitles()).toEqual(['Under', 'Loaded', 'Inside', 'Prompt']);
    await render('Loaded', ['Under', 'Prompt']);
    await render('Again', ['Under', 'Prompt']);
    expect(dialogTitles()).toEqual(['Under', 'Again', 'Prompt']);

    await render('Again', ['Under', 'Prompt'], 'Asked');
    expect(dialogTitles()).toEqual(['Under', 'Again', 'Prompt', 'Asked']);
    await render('Last', ['Under', 'Prompt']);
    expect(dialogTitles()).toEqual(['Under', 'Last', 'Prompt']);
  });

  it('keep their places in two slots, also when one is swapped for two', async () => {
    const slots: OverlaySlot[] = [{ openCount: 0, order: null }, { openCount: 0, order: null }];
    const render = (...titles: (string | null)[]) => act(async () => root.render(titles.map((title, index) => (
      <OverlaySlotContext.Provider key={index} value={slots[index]}>
        {title && (
          <Modal key={title} isOpen onClose={() => {}} title={title}>
            {title === 'Loaded' && <Modal isOpen onClose={() => {}} title="Inside">Inside</Modal>}
          </Modal>
        )}
      </OverlaySlotContext.Provider>
    ))));
    await render('Loading', null);
    await render('Loading', 'Prompt');
    await render('Loaded', 'Prompt');

    await render('Loaded', 'Warning');

    expect(dialogTitles()).toEqual(['Loaded', 'Inside', 'Warning']);
  });

  it('tell the page whether any is open', async () => {
    const told: boolean[] = [];
    function Page({ open }: { open: boolean }) {
      told.push(useIsOverlayOpen());
      return <Modal isOpen={open} onClose={() => {}} title="First">First</Modal>;
    }

    for (const open of [false, true, false]) {
      await act(async () => root.render(<Page open={open} />));
      expect(told.at(-1)).toBe(open);
    }
  });

  it('keep the page from scrolling until the last closes', async () => {
    await show({ first: true, second: true });
    expect(document.body.style.overflow).toBe('hidden');

    await show({ second: true });
    expect(document.body.style.overflow).toBe('hidden');

    await show({});
    expect(document.body.style.overflow).toBe('');
  });
});

describe('a dialog\'s focus', () => {
  let root: Root;

  function show(open: boolean, children: ReactNode = <button id="only" />): Promise<void> {
    return act(async () => root.render(
      <>
        <button id="behind" />
        <Modal isOpen={open} onClose={() => {}} title="Dialog">
          {children}
        </Modal>
        <button id="after" />
      </>,
    ));
  }

  function element(id: string): HTMLElement {
    return document.getElementById(id)!;
  }

  function closeButton(): Element | null {
    return document.querySelector('button[aria-label="Close"]');
  }

  function tab(shiftKey = false): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true });
    act(() => void (document.activeElement ?? document.body).dispatchEvent(event));
    return event;
  }

  beforeEach(async () => {
    root = createRoot(document.body.appendChild(document.createElement('div')));
    await show(false);
    element('behind').focus();
    await show(true);
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
  });

  it('goes into it as it opens, and back to where it was as it closes', async () => {
    expect(document.activeElement).toBe(document.querySelector('[role="dialog"]'));

    await show(false);

    expect(document.activeElement).toBe(element('behind'));
  });

  it('goes into it again as it opens again', async () => {
    await show(false);
    await show(true);

    expect(document.activeElement).toBe(document.querySelector('[role="dialog"]'));
  });

  it('goes back to where it was in it as a dialog over it closes', async () => {
    const over = (open: boolean) => show(true, <><button id="only" /><Modal isOpen={open} onClose={() => {}} title="Over">Over</Modal></>);
    await over(false);
    element('only').focus();

    await over(true);
    expect(document.activeElement).not.toBe(element('only'));
    await over(false);

    expect(document.activeElement).toBe(element('only'));
  });

  it('stays where it went meanwhile, as into a dialog that stays, as it closes', async () => {
    element('after').focus();

    await show(false);

    expect(document.activeElement).toBe(element('after'));
  });

  it('goes around within it on Tab, either way', () => {
    element('only').focus();
    expect(tab().defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(closeButton());

    expect(tab(true).defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(element('only'));
  });

  it.each([
    ['a frame', <iframe id="stop" />],
    ['a video', <video id="stop" controls />],
    // Drawn, as happy-dom draws no recording's player.
    ['a recording', <audio id="stop" controls style={{ display: 'block' }} />],
  ])('stops at %s', async (_what, stop) => {
    await show(true, stop);
    (closeButton() as HTMLElement).focus();

    tab(true);

    expect(document.activeElement).toBe(element('stop'));
  });

  it('does not stop at a button that is disabled or not shown', async () => {
    await show(true, <><button id="only" /><button disabled /><div style={{ display: 'none' }}><button /></div></>);
    (closeButton() as HTMLElement).focus();

    tab(true);

    expect(document.activeElement).toBe(element('only'));
  });

  it('comes back into it on Tab once what had it is gone', () => {
    (document.activeElement as HTMLElement).blur();

    tab();

    expect(document.activeElement).toBe(closeButton());
  });

  it('is left alone on Tab between its stops, and in a menu beside it', () => {
    (closeButton() as HTMLElement).focus();
    expect(tab().defaultPrevented).toBe(false);

    element('after').focus();
    expect(tab().defaultPrevented).toBe(false);
  });
});
