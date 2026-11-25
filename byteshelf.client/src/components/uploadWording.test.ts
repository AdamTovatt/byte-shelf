import { describe, expect, it } from 'vitest';
import type { Upload } from '../services/uploadQueue';
import { uploadFailure, uploadStage } from './uploadWording';

const underway = { replacing: false, cancel: () => undefined };

function upload(folderPath: string): Upload {
  return { id: 1, shelf: 'docs', folderPath, file: new File(['new'], 'a.txt'), name: 'a.txt' };
}

describe('what an upload\'s row shows of it on its way', () => {
  it('says it is waiting, then encrypting, with nothing of the bar filled', () => {
    expect(uploadStage({ status: 'waiting', ...underway })).toEqual({ label: 'Waiting', progress: 0, finishing: false });
    expect(uploadStage({ status: 'encrypting', ...underway })).toEqual({ label: 'Encrypting', progress: 0, finishing: false });
  });

  it('says how much is sent of all, with as much of the bar filled', () => {
    expect(uploadStage({ status: 'uploading', sentBytes: 1024, totalBytes: 4096, ...underway })).toEqual({ label: '1 KiB of 4 KiB', progress: 25, finishing: false });
  });

  it('says it is finishing once all is sent', () => {
    expect(uploadStage({ status: 'uploading', sentBytes: 4096, totalBytes: 4096, ...underway })).toEqual({ label: 'Finishing', progress: 100, finishing: true });
  });
});

describe('why an upload was not stored', () => {
  const taken = { status: 'taken', replace: () => undefined } as const;

  it('says "here" on its row, and names its folder elsewhere, a shelf\'s root by the shelf', () => {
    expect(uploadFailure(taken, null)).toBe('A file with this name is already here.');
    expect(uploadFailure(taken, upload('/a'))).toBe('A file with this name is already in /a.');
    expect(uploadFailure(taken, upload('/'))).toBe('A file with this name is already in docs.');
  });

  it('says why replacing the file failed, and a failure\'s own reason', () => {
    expect(uploadFailure({ ...taken, failure: new Error('Full.') }, upload('/a'))).toBe('Could not replace the file in /a: Full.');
    expect(uploadFailure({ status: 'failed', error: new Error('Down.') }, null)).toBe('Down.');
  });
});
