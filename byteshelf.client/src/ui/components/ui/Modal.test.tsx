// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { Modal } from './Modal';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('a dialog\'s focus', () => {
  let root: Root;

  function show(open: boolean): Promise<void> {
    return act(async () => root.render(
      <>
        <button id="behind" />
        <Modal isOpen={open} onClose={() => {}} title="Dialog">
          <button id="only" />
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

  it('goes around within it on Tab, either way', () => {
    element('only').focus();
    expect(tab().defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(closeButton());

    expect(tab(true).defaultPrevented).toBe(true);
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
