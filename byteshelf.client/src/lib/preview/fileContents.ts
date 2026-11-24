import type { ReadableFile } from '../../services/fileReading';
import type { ShelfFile } from '../../services/shelfService';
import { untilAborted } from '../abort';
import { streamBlob } from '../streamBlob';

/** A file up to this size is read into the page at once, a larger one only when asked. */
const READ_UNASKED_BYTES = 100 * 1024 * 1024;

/** Work under way on a file's contents; null when they turn out locked, or stay so. */
type Work = Promise<Blob | null>;

/** Where a file's whole contents are, on their way into the page. */
export type FileContentsState =
  /** Not open yet. */
  | { status: 'unread' }
  /** Large enough that reading it into the page waits to be asked. */
  | { status: 'waiting' }
  | { status: 'reading'; work: Work }
  | { status: 'failed'; error: unknown }
  /** Encrypted, and no password was given; `error` is why unlocking it failed, null when it did not. */
  | { status: 'locked'; error: unknown }
  /** Its password is being asked for. */
  | { status: 'unlocking'; work: Work }
  | { status: 'read'; contents: Blob };

/** Tells one file's contents from another's: the same file renamed or described keeps them. */
export function contentsKey(file: ShelfFile): string {
  return `${file.id} ${file.sha256}`;
}

/**
 * One file's whole contents, read into the page and unlocked once for the preview that shows them and for
 * whatever saves or opens the file. The preview opens it and closes it; what is being read, or asked for,
 * is dropped then, unless the file is being saved or opened.
 */
export class FileContents {
  private file: ReadableFile;
  private now: FileContentsState;
  private readonly listeners = new Set<() => void>();
  private isOpen = false;
  /** Whether the password is asked for when reading the file finds it locked. */
  private asksPassword = false;
  private saves = 0;
  private dropping = new AbortController();

  constructor(file: ReadableFile) {
    this.file = file;
    this.now = this.initial();
  }

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  readonly state = (): FileContentsState => this.now;

  /** Goes on with the file's latest record: renamed or moved, its contents are the same. */
  update(file: ReadableFile): void {
    this.file = file;
  }

  /** Starts reading the file, unless it is too large to unasked; its password is asked for when `asksPassword`. */
  open(asksPassword: boolean): void {
    this.isOpen = true;
    this.asksPassword = asksPassword;
    if (this.now.status === 'unread') this.load();
  }

  close(): void {
    this.isOpen = false;
    this.dropUnwanted();
  }

  /** Reads the file, when it waits to be asked or could not be read. */
  load(): void {
    void this.joinReading();
  }

  /** Asks for the password of a locked file, and reads it. */
  unlock(): void {
    void this.joinUnlocking();
  }

  /** The contents, to save or open the file with: read and unlocked first when they are not yet, unless `signal` aborts. Null when no password was given. */
  async whole(signal?: AbortSignal): Promise<Blob | null> {
    this.saves++;
    try {
      return (await untilAborted(this.joinReading(), signal)) ?? (await untilAborted(this.joinUnlocking(), signal));
    } finally {
      this.saves--;
      if (!this.isOpen) this.dropUnwanted();
    }
  }

  private dropUnwanted(): void {
    if (this.saves > 0) return;
    this.dropping.abort();
    this.dropping = new AbortController();
    this.now = this.initial();
  }

  private initial(): FileContentsState {
    return { status: this.file.file.sizeBytes > READ_UNASKED_BYTES ? 'waiting' : 'unread' };
  }

  private become(state: FileContentsState): void {
    this.now = state;
    for (const listener of this.listeners) listener();
  }

  /** What reading the file answers, joined while it is under way: null at once when it is locked. */
  private joinReading(): Work {
    const { now } = this;
    if (now.status === 'read') return Promise.resolve(now.contents);
    if (now.status === 'reading') return now.work;
    if (now.status === 'locked' || now.status === 'unlocking') return Promise.resolve(null);
    return this.start('reading', (signal) => this.read(signal));
  }

  /** What unlocking a locked file answers, joined while it is under way. */
  private joinUnlocking(): Work {
    const { now } = this;
    if (now.status === 'locked') return this.start('unlocking', (signal) => this.readUnlocked(signal));
    return now.status === 'unlocking' ? now.work : this.joinReading();
  }

  private start(status: 'reading' | 'unlocking', run: (signal: AbortSignal) => Work): Work {
    const work = run(this.dropping.signal);
    // Why it failed is in the state, also when no one waits for it.
    work.catch(() => undefined);
    this.become({ status, work });
    return work;
  }

  private async read(signal: AbortSignal): Work {
    try {
      const contents = await this.file.reading.read(this.file.file, signal);
      if (signal.aborted) return null;
      this.become(contents ? { status: 'read', contents } : { status: 'locked', error: null });
      if (!contents && this.asksPassword) this.unlock();
      return contents;
    } catch (error) {
      if (signal.aborted) return null;
      this.become({ status: 'failed', error });
      throw error;
    }
  }

  private async readUnlocked(signal: AbortSignal): Work {
    try {
      const { file, reading } = this.file;
      const plain = await reading.unlock(file, signal);
      if (signal.aborted) return null;
      if (!plain) {
        this.become({ status: 'locked', error: null });
        return null;
      }
      if (this.now.status === 'unlocking') this.become({ status: 'reading', work: this.now.work });
      const contents = await streamBlob(plain, file.contentType);
      if (signal.aborted) return null;
      this.become({ status: 'read', contents });
      return contents;
    } catch (error) {
      if (signal.aborted) return null;
      this.become({ status: 'locked', error });
      throw error;
    }
  }
}
