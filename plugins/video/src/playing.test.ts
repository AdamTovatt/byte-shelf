import { describe, expect, it } from 'vitest';
import type { DerivedStatus } from '@byteshelf/plugin';
import { asksForStream, playingOf } from './playing';

const NOTHING_FAILED = { streamFailed: false, originalFailed: false, wantsOriginal: false };

describe('what the player plays', () => {
  it('is the stream when the listing names one, and the original once it is chosen, with nothing said', () => {
    const of = (wantsOriginal: boolean) => playingOf({ listed: true, streamsWell: false, status: null, ...NOTHING_FAILED, wantsOriginal });

    expect([of(false), of(true)]).toEqual([
      { source: 'stream', canChooseOriginal: true, note: null },
      { source: 'original', canChooseOriginal: true, note: null },
    ]);
  });

  it('is the other of the stream and the original when one does not play, and nothing when neither does', () => {
    const of = (failed: { streamFailed?: boolean; originalFailed?: boolean }) => playingOf({ listed: true, streamsWell: false, status: null, ...NOTHING_FAILED, wantsOriginal: true, ...failed });

    expect([of({ streamFailed: true }), of({ originalFailed: true }), of({ streamFailed: true, originalFailed: true })]).toEqual([
      { source: 'original', canChooseOriginal: false, note: null },
      { source: 'stream', canChooseOriginal: false, note: null },
      { source: null, canChooseOriginal: false, note: null },
    ]);
  });

  it('is the original of one with no stream listed, and nothing once that does not play', () => {
    expect([
      playingOf({ listed: false, streamsWell: true, status: null, ...NOTHING_FAILED }),
      playingOf({ listed: false, streamsWell: true, status: null, ...NOTHING_FAILED, originalFailed: true }),
    ]).toEqual([
      { source: 'original', canChooseOriginal: false, note: null },
      { source: null, canChooseOriginal: false, note: null },
    ]);
  });

  it('says of a stream not there only that it is on its way or that there is no room', () => {
    const of = (status: DerivedStatus | null) => playingOf({ listed: false, streamsWell: false, status, ...NOTHING_FAILED }).note;

    expect([of(null), of('making'), of('noRoom'), of('none'), of('made')]).toEqual([
      null,
      'A streaming copy is on its way.',
      'There is no room on this shelf for a streaming copy.',
      null,
      null,
    ]);
  });
});

describe('whether the player asks for a stream', () => {
  it('is only of one not listed, of a video the reader says does not stream well as it is', () => {
    expect([
      asksForStream({ listed: false, streamsWell: false }),
      asksForStream({ listed: true, streamsWell: false }),
      asksForStream({ listed: false, streamsWell: true }),
      asksForStream({ listed: false, streamsWell: null }),
    ]).toEqual([true, false, false, false]);
  });
});
