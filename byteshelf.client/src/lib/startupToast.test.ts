// @vitest-environment happy-dom
import { afterEach, expect, it, vi } from 'vitest';
import { leaveStartupToast, takeStartupToast } from './startupToast';

afterEach(() => {
  vi.unstubAllGlobals();
});

it('is taken once', () => {
  leaveStartupToast('Paired.');

  expect(takeStartupToast()).toBe('Paired.');
  expect(takeStartupToast()).toBeNull();
});

it('is lost, and nothing fails, where nothing can be kept', () => {
  vi.stubGlobal('sessionStorage', { getItem: refuse, setItem: refuse, removeItem: refuse });

  leaveStartupToast('Paired.');

  expect(takeStartupToast()).toBeNull();
});

function refuse(): never {
  throw new DOMException('It may not.', 'SecurityError');
}
