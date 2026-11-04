// @vitest-environment happy-dom
import type { ReactNode } from 'react';
import toast from 'react-hot-toast';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fileOpening, type FileOpening } from './fileOpening';
import { saveTokens } from '../services/authService';
import { deletedContent, liveContent } from '../services/contentService';
import type { DeletionTree, ShelfFile } from '../services/shelfService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';
import { textOf } from '../test/dom';

/** What was shown or saved from a blob here, and saved by navigating; whether a tab may show one. */
const blobs = vi.hoisted(() => ({ shown: [] as string[], saved: [] as string[], savedFrom: [] as string[], tabBlocked: false }));

vi.mock('../lib/localBlobs', () => ({
  showBlob: (blob: Blob, name: string) => {
    if (blobs.tabBlocked) return false;
    blobs.shown.push(`${name} ${blob.type}`);
    return true;
  },
  saveBlob: (_blob: Blob, name: string) => {
    blobs.saved.push(name);
  },
  saveFrom: (href: string) => {
    blobs.savedFrom.push(href);
  },
}));

/** The tabs opened, or none when the browser blocks them. */
const tabs = vi.hoisted(() => ({ opened: [] as { location: { href: string }; closed: boolean; close: () => void }[], blocked: false }));

vi.mock('../lib/encryption/passwords', () => ({
  openTabWithoutPasswords: () => {
    if (tabs.blocked) return null;
    const tab = { location: { href: '' }, closed: false, close: () => { tab.closed = true; } };
    tabs.opened.push(tab);
    return tab;
  },
}));

/** The action toasts shown: their text, and the action that pressing their button runs. */
const toasts = vi.hoisted(() => ({ shown: [] as { message: ReactNode; action: string; onAction: () => void }[] }));

vi.mock('./actionToast', () => ({
  showActionToast: (shown: { message: ReactNode; action: string; onAction: () => void }) => {
    toasts.shown.push(shown);
  },
}));

function file(encrypted = false, id = 'f'): ShelfFile {
  return { id, shelfId: 's', directoryId: 'd', path: `/${id}.txt`, name: `${id}.txt`, contentType: 'text/plain', sizeBytes: 12, sha256: 'x', createdAt: '', updatedAt: '', description: null, encrypted };
}

/** Deletion `deletionId`, which took `file()`. */
function deletion(deletionId: string): DeletionTree {
  const root = { id: 'd', shelfId: 's', parentId: null, path: '/', name: '', createdAt: '', encrypted: false };
  return {
    deletion: { deletionId, kind: 'file', entryId: 'f', path: '/f.txt', deletedAt: '', purgesAt: '', fileCount: 1, sizeBytes: 12 },
    tree: { directory: root, directories: [], files: [file()] },
  };
}

describe('opening and saving files', () => {
  let opening: FileOpening;
  let errors: string[];
  const decrypted: string[] = [];
  /** Whether the password prompt is cancelled, so nothing is decrypted. */
  let promptCancelled: boolean;

  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 'first', expiresAt: inAnHour(), refreshToken: 'refresh-1' });
    blobs.shown = [];
    blobs.saved = [];
    blobs.savedFrom = [];
    blobs.tabBlocked = false;
    tabs.opened = [];
    tabs.blocked = false;
    toasts.shown = [];
    decrypted.length = 0;
    promptCancelled = false;
    errors = [];
    vi.spyOn(toast, 'error').mockImplementation((message) => {
      errors.push(String(message));
      return '';
    });
    opening = fileOpening(liveContent('docs'), async (encrypted) => {
      if (promptCancelled) return null;
      decrypted.push(encrypted.id);
      return new Blob(['secret'], { type: encrypted.contentType });
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  describe('over HTTPS, where the session cookie is kept', () => {
    beforeEach(() => {
      vi.stubGlobal('isSecureContext', true);
    });

    it('opens a plain file by navigating a tab to it, reading nothing here and making no link', async () => {
      const sent = stubServer(async () => json({}));

      await opening.open(file());

      expect(tabs.opened.map((tab) => tab.location.href)).toEqual(['/api/open/docs/content?id=f']);
      expect(sent).toEqual([]);
      expect(blobs.shown).toEqual([]);
    });

    it('saves a plain file by navigating to it as a download', async () => {
      stubServer(async () => json({}));

      await opening.save([file()]);

      expect(blobs.savedFrom).toEqual(['/api/open/docs/content?id=f&download=true']);
      expect(blobs.saved).toEqual([]);
    });

    it('saves a file from the trash by navigating to its deletion, as a live file is saved', async () => {
      stubServer(async () => json({}));

      await fileOpening(deletedContent('docs', deletion('del')), async () => null).save([file()]);

      expect(blobs.savedFrom).toEqual(['/api/open/docs/trash/del/content?id=f&download=true']);
    });

    it('saves plain files by navigating and encrypted ones from their contents decrypted here, together', async () => {
      stubServer(async () => json({}));

      await opening.save([file(false, 'plain'), file(true, 'secret')]);

      expect(blobs.savedFrom).toEqual(['/api/open/docs/content?id=plain&download=true']);
      expect(decrypted).toEqual(['secret']);
      expect(blobs.saved).toEqual(['secret.txt']);
    });

    it('renews a token about to run out before navigating, so the cookie the tab sends is current', async () => {
      saveTokens({ token: 'first', expiresAt: new Date(Date.now() + 10_000).toISOString(), refreshToken: 'refresh-1' });
      const sent = stubServer(async () => json({ token: 'second', expiresAt: inAnHour(), refreshToken: 'refresh-2' }));

      await opening.open(file());

      expect(sent).toEqual(['POST /api/auth/refresh']);
      expect(tabs.opened[0].location.href).toBe('/api/open/docs/content?id=f');
    });

    it('renews a session saved before the server set the cookie, once, before navigating', async () => {
      localStorage.clear();
      localStorage.setItem('byteshelf_auth_token', 'first');
      localStorage.setItem('byteshelf_auth_expires_at', inAnHour());
      localStorage.setItem('byteshelf_auth_refresh_token', 'refresh-1');
      const sent = stubServer(async () => json({ token: 'second', expiresAt: inAnHour(), refreshToken: 'refresh-2' }));

      await opening.open(file());
      await opening.open(file());

      expect(sent).toEqual(['POST /api/auth/refresh']);
      expect(tabs.opened.map((tab) => tab.location.href)).toEqual(['/api/open/docs/content?id=f', '/api/open/docs/content?id=f']);
    });

    it('closes the tab rather than navigating it when the session has ended, and says so', async () => {
      localStorage.clear();
      stubServer(async () => json({}));

      await opening.open(file());

      expect(tabs.opened.map((tab) => [tab.location.href, tab.closed])).toEqual([['', true]]);
      expect(errors).toEqual(['The session has ended. Sign in again to open files.']);
    });

    it('saves a plain file by navigating when the browser blocks the tab', async () => {
      tabs.blocked = true;
      stubServer(async () => json({}));

      await opening.open(file());

      expect(blobs.savedFrom).toEqual(['/api/open/docs/content?id=f&download=true']);
    });

    it('opens an encrypted file from its contents decrypted here, never by navigating', async () => {
      stubServer(async () => json({}));

      await opening.open(file(true));

      expect(decrypted).toEqual(['f']);
      expect(blobs.shown).toEqual(['f.txt text/plain']);
      expect(tabs.opened).toEqual([]);
    });

    it('offers an unlocked file from a toast when the browser blocks its tab', async () => {
      blobs.tabBlocked = true;
      stubServer(async () => json({}));

      await opening.open(file(true));

      expect(toasts.shown.map((shown) => [textOf(shown.message), shown.action])).toEqual([['f.txt is unlocked', 'Open']]);
      blobs.tabBlocked = false;
      toasts.shown[0].onAction();
      expect(blobs.shown).toEqual(['f.txt text/plain']);
    });

    it('neither opens nor saves an encrypted file, nor reports an error, when its password prompt is cancelled', async () => {
      promptCancelled = true;
      stubServer(async () => json({}));

      await opening.open(file(true));
      await opening.save([file(true)]);

      expect([blobs.shown, blobs.saved, toasts.shown, errors]).toEqual([[], [], [], []]);
    });
  });

  describe('over plain HTTP, where the session cookie is refused', () => {
    beforeEach(() => {
      vi.stubGlobal('isSecureContext', false);
    });

    it('opens a plain file from its contents read here with the token, as an encrypted one is', async () => {
      const sent = stubServer(async () => new Response('the contents', { headers: { 'Content-Type': 'text/plain' } }));

      await opening.open(file());

      expect(sent).toEqual(['GET /api/shelves/docs/content?id=f Bearer first']);
      expect(blobs.shown).toEqual(['f.txt text/plain']);
      expect(tabs.opened).toEqual([]);
    });

    it('offers a plain file read here from a toast when the browser blocks its tab', async () => {
      blobs.tabBlocked = true;
      stubServer(async () => new Response('the contents', { headers: { 'Content-Type': 'text/plain' } }));

      await opening.open(file());

      expect(toasts.shown.map((shown) => [textOf(shown.message), shown.action])).toEqual([['f.txt is ready', 'Open']]);
    });

    it('saves a file from the trash from its contents read here from its deletion', async () => {
      const sent = stubServer(async () => new Response('the contents', { headers: { 'Content-Type': 'text/plain' } }));

      await fileOpening(deletedContent('docs', deletion('del')), async () => null).save([file()]);

      expect(sent).toEqual(['GET /api/shelves/docs/trash/del/content?id=f Bearer first']);
      expect(blobs.saved).toEqual(['f.txt']);
    });

    it('saves a plain file from its contents read here, never by navigating', async () => {
      stubServer(async () => new Response('the contents', { headers: { 'Content-Type': 'text/plain' } }));

      await opening.save([file()]);

      expect(blobs.saved).toEqual(['f.txt']);
      expect(blobs.savedFrom).toEqual([]);
    });
  });
});
