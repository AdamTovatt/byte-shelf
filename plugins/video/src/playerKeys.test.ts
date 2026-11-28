// @vitest-environment happy-dom
import { describe, expect, it } from 'vitest';
import { keyAction } from './playerKeys';

describe('the keys of the player', () => {
  const stage = document.createElement('div');

  it('play or pause with Space and K, and jump 10 seconds back and forward with J and L, either case', () => {
    expect([' ', 'k', 'K', 'j', 'l', 'L'].map((key) => keyAction(key, stage))).toEqual([
      { kind: 'toggle' },
      { kind: 'toggle' },
      { kind: 'toggle' },
      { kind: 'jump', seconds: -10 },
      { kind: 'jump', seconds: 10 },
      { kind: 'jump', seconds: 10 },
    ]);
  });

  it('leave the arrow keys to the seek bar and to stepping between files', () => {
    expect(['ArrowLeft', 'ArrowRight', 'Enter', 'x'].map((key) => keyAction(key, stage))).toEqual([null, null, null, null]);
  });

  it('leave Space on a button to the button, but for K', () => {
    const button = document.createElement('button');
    const inButton = button.appendChild(document.createElement('span'));

    expect([keyAction(' ', button), keyAction(' ', inButton), keyAction('k', button)]).toEqual([null, null, { kind: 'toggle' }]);
  });

  it('do nothing in a field', () => {
    const field = document.createElement('input');

    expect([keyAction('k', field), keyAction(' ', document.createElement('textarea')), keyAction('j', field)]).toEqual([null, null, null]);
  });
});
