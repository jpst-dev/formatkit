import { micromark } from 'micromark';

/** Release notes -> HTML. Raw HTML in the markdown is escaped (micromark default), never passed through. */
export function renderNotes(markdown: string): string {
  const html = micromark(markdown, { allowDangerousHtml: false, allowDangerousProtocol: false });
  // Notes sit under the release's own h2, so their headings go one level down (never above h3).
  return html.replace(/<(\/?)h([1-5])>/g, (_m, close: string, n: string) => `<${close}h${Math.max(3, Number(n) + 1)}>`);
}
