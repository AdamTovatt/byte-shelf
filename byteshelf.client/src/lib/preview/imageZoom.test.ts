import { describe, expect, it } from 'vitest';
import { FITTED, movedBy, toggledAt, zoomedAt } from './imageZoom';

const frame = { width: 400, height: 300 };
/** An image that fills the frame's width, and not its height. */
const fitted = { width: 400, height: 200 };

describe('the image\'s zoom', () => {
  it('keeps what is under the pointer there', () => {
    const point = { x: 100, y: 50 };

    const view = zoomedAt(FITTED, 2, point, fitted, frame);

    // What was 100 right of the middle is 200 right of it at twice the size, so the image moves 100 left.
    expect(view).toEqual({ scale: 2, x: -100, y: -50 });
  });

  it('never zooms out past fitted, where the image is centred', () => {
    expect(zoomedAt({ scale: 2, x: 150, y: 0 }, 0.1, { x: 0, y: 0 }, fitted, frame)).toEqual(FITTED);
  });

  it('never zooms in past the limit', () => {
    expect(zoomedAt(FITTED, 1000, { x: 0, y: 0 }, fitted, frame).scale).toBe(16);
  });

  it('moves a zoomed image only until its edge reaches the frame\'s, and a side that fits not at all', () => {
    expect(movedBy({ scale: 2, x: 0, y: 0 }, { x: 900, y: -900 }, fitted, frame)).toEqual({ scale: 2, x: 200, y: -50 });
    expect(movedBy(FITTED, { x: 10, y: 10 }, fitted, frame)).toEqual(FITTED);
  });

  it('zooms in on a double-click and back to fitted on the next', () => {
    const zoomed = toggledAt(FITTED, { x: 0, y: 0 }, fitted, frame);

    expect(zoomed.scale).toBeGreaterThan(1);
    expect(toggledAt(zoomed, { x: 50, y: 50 }, fitted, frame)).toEqual(FITTED);
  });
});
