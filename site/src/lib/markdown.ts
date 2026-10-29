import { micromark } from 'micromark';

/** Release notes -> HTML. Raw HTML in the markdown is escaped (micromark default), never passed through. */
export function renderNotes(markdown: string): string {
  return micromark(markdown, { allowDangerousHtml: false, allowDangerousProtocol: false });
}
