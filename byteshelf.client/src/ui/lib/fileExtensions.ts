// File extensions shared by the file icons and anything else that tells file kinds apart by name.

export const IMAGE_EXTENSIONS = ['.png', '.jpg', '.jpeg', '.gif', '.webp', '.svg', '.bmp'];
export const TEXT_DOCUMENT_EXTENSIONS = ['.md', '.txt'];
export const PDF_EXTENSIONS = ['.pdf'];

const WORD_DOCUMENT_EXTENSIONS = ['.docx', '.dotx', '.doc', '.dot', '.docm', '.dotm'];

export function isWordDocumentFileName(fileName: string): boolean {
  const lower = fileName.toLowerCase();
  return WORD_DOCUMENT_EXTENSIONS.some((extension) => lower.endsWith(extension));
}
