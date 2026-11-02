// @vitest-environment happy-dom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { showBlob, showsInTab } from './localBlobs';

describe('decrypted files in a tab', () => {
  afterEach(() => vi.restoreAllMocks());

  it.each(['image/png', 'image/jpeg', 'application/pdf', 'text/plain; charset=utf-8', 'audio/mpeg', 'video/mp4'])('shows %s', (type) => {
    expect(showsInTab(type)).toBe(true);
  });

  // A blob's URL is the app's own origin, so a type a browser runs would run as the app.
  it.each(['text/html', 'image/svg+xml', 'application/xhtml+xml', 'text/xml', 'application/octet-stream', ''])('saves %s', (type) => {
    expect(showsInTab(type)).toBe(false);
  });

  it('saves a type it does not show, under its name, without opening a tab', () => {
    const open = vi.spyOn(window, 'open');
    const saved: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      saved.push(this.download);
    });

    expect(showBlob(new Blob(['<p>'], { type: 'text/html' }), 'page.html')).toBe(true);
    expect(saved).toEqual(['page.html']);
    expect(open).not.toHaveBeenCalled();
  });

  it('opens a type it shows in a tab without this tab\'s kept passwords', () => {
    /** The copy of this tab's sessionStorage a new tab starts with. */
    class CopiedStorage {
      'byteshelf.password.docs/folder-1' = 'pw';
      theme = 'dark';
      removeItem(key: string): void {
        delete (this as unknown as Record<string, string>)[key];
      }
    }
    const tab = { sessionStorage: new CopiedStorage(), location: { href: '' }, close: () => {} };
    vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window);

    expect(showBlob(new Blob(['a'], { type: 'text/plain' }), 'a.txt')).toBe(true);
    expect(Object.keys(tab.sessionStorage)).toEqual(['theme']);
    expect(tab.location.href).toMatch(/^blob:/);
  });

  it('answers false when the browser blocks the tab', () => {
    vi.spyOn(window, 'open').mockReturnValue(null);

    expect(showBlob(new Blob(['a'], { type: 'text/plain' }), 'a.txt')).toBe(false);
  });
});
