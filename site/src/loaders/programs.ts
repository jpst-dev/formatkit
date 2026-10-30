import { readFile } from 'node:fs/promises';
import type { Loader } from 'astro/loaders';

export type ProgramText = { name: string; description: string };
export type Program = {
  id: string;
  category: string;
  source: string;
  wingetId?: string;
  text: Record<string, ProgramText>;
};

const LANGS = ['en', 'pt-BR'] as const;
const FILE = '../src/FormatKit.Programs/Catalog/Data/programs.json';

type Raw = { id?: unknown; category?: unknown; source?: unknown; text?: Record<string, Record<string, unknown>> } & Record<string, unknown>;

/** Missing or duplicate id, or a missing en/pt-BR name, throws with the id. */
export function loadPrograms(json: unknown): Program[] {
  const raw = (json as { programs?: Raw[] } | null)?.programs;
  if (!Array.isArray(raw)) throw new Error('programs.json has no "programs" array');
  const seen = new Set<string>();
  return raw.map((r, index) => {
    if (typeof r.id !== 'string' || !r.id) throw new Error(`programs.json entry #${index} has no id`);
    const key = r.id.toLowerCase();
    if (seen.has(key)) throw new Error(`programs.json: duplicate id "${r.id}"`);
    seen.add(key);
    for (const lang of LANGS) {
      const name = r.text?.[lang]?.name;
      if (typeof name !== 'string' || !name) throw new Error(`programs.json: program "${r.id}" has no ${lang} name`);
    }
    const text = Object.fromEntries(
      Object.entries(r.text ?? {}).map(([lang, v]) => [
        lang,
        { ...v, name: String(v.name ?? ''), description: typeof v.description === 'string' ? v.description : '' },
      ]),
    ) as Record<string, ProgramText>;
    return {
      ...r,
      id: r.id,
      category: typeof r.category === 'string' ? r.category : '',
      source: typeof r.source === 'string' ? r.source : '',
      text,
    } as Program;
  });
}

export function programsLoader(): Loader {
  return {
    name: 'fk-programs',
    async load({ store, parseData }) {
      const programs = loadPrograms(JSON.parse(await readFile(FILE, 'utf8')));
      store.clear();
      for (const p of programs) store.set({ id: p.id, data: await parseData({ id: p.id, data: p }) });
    },
  };
}
