import { describe, expect, it } from 'vitest';
import { frameAt, recordedVideo } from './recordedVideo';

const STORYBOARD = { intervalSeconds: 5.4, columns: 10, rows: 10, frameWidth: 160, frameHeight: 90, count: 100 };

describe('what the reader recorded of a video', () => {
  it('is whether it streams well and its storyboard, whatever else the document says', () => {
    expect(recordedVideo({ durationSeconds: 540, width: 1920, height: 1080, streamsWell: false, storyboard: STORYBOARD })).toEqual({ streamsWell: false, storyboard: STORYBOARD });
  });

  it('is with no storyboard of a document that says none that can be read', () => {
    expect([
      recordedVideo({ streamsWell: true }),
      recordedVideo({ streamsWell: true, storyboard: { ...STORYBOARD, columns: 0 } }),
      recordedVideo({ streamsWell: true, storyboard: 'grid' }),
    ]).toEqual([
      { streamsWell: true, storyboard: null },
      { streamsWell: true, storyboard: null },
      { streamsWell: true, storyboard: null },
    ]);
  });

  it('is none of a document that does not say whether it streams well, or of none', () => {
    expect([recordedVideo(null), recordedVideo({ streamsWell: 'no', storyboard: STORYBOARD })]).toEqual([null, null]);
  });
});

describe('the storyboard frame of a time', () => {
  it('is of the stretch the time is in, left to right and then down', () => {
    // Into the fourth stretch of the second row: frame 13, at column 3 of row 1.
    const intoFrame13 = 13 * STORYBOARD.intervalSeconds + 0.1;

    expect([frameAt(STORYBOARD, 0), frameAt(STORYBOARD, 5.39), frameAt(STORYBOARD, 5.4), frameAt(STORYBOARD, intoFrame13)]).toEqual([
      { x: 0, y: 0 },
      { x: 0, y: 0 },
      { x: 160, y: 0 },
      { x: 480, y: 90 },
    ]);
  });

  it('is the last at the end and past it, and the first before the start', () => {
    expect([frameAt(STORYBOARD, 540), frameAt(STORYBOARD, 999), frameAt(STORYBOARD, -1)]).toEqual([{ x: 1440, y: 810 }, { x: 1440, y: 810 }, { x: 0, y: 0 }]);
  });
});
