import { describe, expect, it } from 'vitest';
import { keyAction } from './playerKeys';

describe('the keys of the player', () => {
  it('play or pause with Space and K, and jump 10 seconds back and forward with J and L, either case', () => {
    expect([' ', 'k', 'K', 'j', 'l', 'L'].map(keyAction)).toEqual([
      { kind: 'toggle' },
      { kind: 'toggle' },
      { kind: 'toggle' },
      { kind: 'jump', seconds: -10 },
      { kind: 'jump', seconds: 10 },
      { kind: 'jump', seconds: 10 },
    ]);
  });

  it('leave the arrow keys to the seek bar and to stepping between files', () => {
    expect(['ArrowLeft', 'ArrowRight', 'Enter', 'x'].map(keyAction)).toEqual([null, null, null, null]);
  });
});
