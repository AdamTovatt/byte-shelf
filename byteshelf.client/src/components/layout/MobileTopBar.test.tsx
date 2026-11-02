// @vitest-environment happy-dom
import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { MobileTopBarActions, MobileTopBarOverlay } from './MobileTopBar';
import { MobileTopBarContext } from '../../contexts/MobileTopBarContext';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('a page\'s part of the phone\'s top bar', () => {
  let root: Root;
  let page: HTMLDivElement;
  let bar: HTMLDivElement;
  let actions: HTMLDivElement;

  beforeEach(() => {
    page = document.createElement('div');
    bar = document.createElement('div');
    actions = bar.appendChild(document.createElement('div'));
    root = createRoot(page);
  });

  afterEach(() => {
    act(() => root.unmount());
  });

  function render(children: ReactNode, slots?: { actions: HTMLElement | null; bar: HTMLElement | null }): void {
    act(() => root.render(slots ? <MobileTopBarContext.Provider value={slots}>{children}</MobileTopBarContext.Provider> : children));
  }

  it('puts its actions after the menu button, and what covers the bar on the bar itself', () => {
    render(
      <>
        <MobileTopBarActions><span>search</span></MobileTopBarActions>
        <MobileTopBarOverlay><span>2 selected</span></MobileTopBarOverlay>
      </>,
      { actions, bar },
    );

    expect([actions.textContent, bar.lastElementChild?.textContent, page.textContent]).toEqual(['search', '2 selected', '']);
  });

  it('renders nothing where the shell draws no top bar', () => {
    render(
      <>
        <MobileTopBarActions><span>search</span></MobileTopBarActions>
        <MobileTopBarOverlay><span>2 selected</span></MobileTopBarOverlay>
      </>,
    );

    expect([page.textContent, bar.textContent, document.body.textContent]).toEqual(['', '', '']);
  });
});
