import { readFile } from 'node:fs/promises';
import type { Loader } from 'astro/loaders';
import { warn } from '../lib/warnings';

export type KnownIssueText = { title: string; breaks: string; fix: string };
export type KnownIssue = { id: string; severity: string; text: Record<string, KnownIssueText> };

const LANGS = ['en', 'pt-BR'] as const;
const FILE = '../src/FormatKit.Cleanup/Catalog/Data/known-issues.json';

type Raw = { id?: unknown; severity?: unknown; text?: Record<string, Record<string, unknown>> } & Record<string, unknown>;

/** Hard errors: missing or duplicate id, missing title or breaks in en or pt-BR. Anything else unknown is a warning. */
export function loadKnownIssues(json: unknown): KnownIssue[] {
  const raw = (json as { issues?: Raw[] } | null)?.issues;
  if (!Array.isArray(raw)) throw new Error('known-issues.json has no "issues" array');
  const seen = new Set<string>();
  return raw.map((r, index) => {
    if (typeof r.id !== 'string' || !r.id) throw new Error(`known-issues.json entry #${index} has no id`);
    const key = r.id.toLowerCase();
    if (seen.has(key)) throw new Error(`known-issues.json: duplicate id "${r.id}"`);
    seen.add(key);
    for (const lang of LANGS) {
      const text = r.text?.[lang];
      for (const field of ['title', 'breaks'] as const) {
        if (typeof text?.[field] !== 'string' || !text[field]) {
          throw new Error(`known-issues.json: issue "${r.id}" has no ${lang} ${field}`);
        }
      }
      if (typeof text?.fix !== 'string') warn('knownIssue.fix-missing', `${r.id} (${lang})`);
    }
    const text = Object.fromEntries(
      Object.entries(r.text ?? {}).map(([lang, v]) => [
        lang,
        { ...v, title: String(v.title ?? ''), breaks: String(v.breaks ?? ''), fix: typeof v.fix === 'string' ? v.fix : '' },
      ]),
    ) as Record<string, KnownIssueText>;
    const severity = typeof r.severity === 'string' ? r.severity : '';
    return { ...r, id: r.id, severity, text } as KnownIssue;
  });
}

export function knownIssuesLoader(): Loader {
  return {
    name: 'fk-known-issues',
    async load({ store, parseData }) {
      const issues = loadKnownIssues(JSON.parse(await readFile(FILE, 'utf8')));
      store.clear();
      for (const i of issues) store.set({ id: i.id, data: await parseData({ id: i.id, data: i }) });
    },
  };
}
