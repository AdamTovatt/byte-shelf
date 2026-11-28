// @vitest-environment happy-dom
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { KEPT, resumeAt, savePosition, savedPosition } from './resumePoint';

beforeEach(() => {
  localStorage.clear();
  vi.restoreAllMocks();
});

describe('where a video is picked up', () => {
  it('is where it was stopped, more than 10 seconds from either end', () => {
    expect([resumeAt(10.5, 100), resumeAt(89.5, 100)]).toEqual([10.5, 89.5]);
  });

  it('is the start when it was stopped near the start or the end, or not at all', () => {
    expect([resumeAt(10, 100), resumeAt(90, 100), resumeAt(5, 15), resumeAt(null, 100)]).toEqual([null, null, null, null]);
  });
});

describe('where a video was stopped', () => {
  it('is kept per file, the last kept of each', () => {
    savePosition('a', 12);
    savePosition('b', 30);
    savePosition('a', 42);

    expect([savedPosition('a'), savedPosition('b'), savedPosition('c')]).toEqual([42, 30, null]);
  });

  it('is forgotten once it was watched to the end', () => {
    savePosition('a', 12);
    savePosition('a', null);

    expect(savedPosition('a')).toBeNull();
  });

  it('is kept of so many files, the least recently stopped going first', () => {
    for (let file = 0; file <= KEPT; file++) savePosition(`file ${file}`, 20);

    expect([savedPosition('file 0'), savedPosition('file 1'), savedPosition(`file ${KEPT}`)]).toEqual([null, 20, 20]);
  });

  it('is nothing of what another kept there, what was kept before where the browser keeps no more, and nothing where it reads nothing', () => {
    localStorage.setItem('byteshelf-video-positions', '{"not":"a list"}');
    expect(savedPosition('a')).toBeNull();

    savePosition('a', 12);
    vi.spyOn(localStorage, 'setItem').mockImplementation(() => {
      throw new Error('Full.');
    });
    savePosition('a', 42);
    expect(savedPosition('a')).toBe(12);

    vi.spyOn(localStorage, 'getItem').mockImplementation(() => {
      throw new Error('Refused.');
    });
    expect(savedPosition('a')).toBeNull();
  });
});
