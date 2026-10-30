import { readFile } from 'node:fs/promises';
import type { Loader } from 'astro/loaders';
import { KNOWN_CATEGORIES, levelRank } from '../lib/labels';
import { warn } from '../lib/warnings';

export type ItemText = { title: string; what: string; breaks: string; gain?: string };
export type Evidence = { level: string; sources: { title: string; url: string }[] } | null;
export type CleanupAction = { type: string } & Record<string, unknown>;
export type CleanupItem = {
  id: string;
  category: string;
  level: string;
  scope: string;
  contested: boolean;
  requiresReboot: boolean;
  laptopSensitive: boolean;
  optIn: boolean;
  text: Record<'en' | 'pt-BR', ItemText>;
  actions: CleanupAction[];
  evidence: Evidence;
  order: number;
};

const LANGS = ['en', 'pt-BR'] as const;
const KNOWN_ACTIONS = ['appx', 'registry', 'named', 'powerScheme', 'powerSetting', 'scheduledTask', 'service', 'startupItem'];
const FILE = '../src/FormatKit.Cleanup/Catalog/Data/cleanup.json';

type Raw = Record<string, unknown> & { text?: Record<string, Record<string, unknown>> };

const str = (v: unknown, fallback = ''): string => (typeof v === 'string' ? v : fallback);

function parseEvidence(id: string, v: unknown): Evidence {
  if (v === undefined || v === null) return null;
  if (typeof v === 'string') return { level: v, sources: [] };
  if (typeof v === 'object' && !Array.isArray(v) && typeof (v as { level?: unknown }).level === 'string') {
    const o = v as { level: string; sources?: unknown };
    if (o.sources === undefined) return { level: o.level, sources: [] };
    if (
      Array.isArray(o.sources) &&
      o.sources.every((s) => s && typeof s.title === 'string' && typeof s.url === 'string')
    ) {
      return { level: o.level, sources: o.sources.map((s: { title: string; url: string }) => ({ title: s.title, url: s.url })) };
    }
  }
  warn('evidence.shape', id);
  return null;
}

/** Hard errors only (D5): id problems and missing title/what/breaks. Everything else unknown warns or passes through. */
export function loadCleanup(json: unknown): CleanupItem[] {
  const doc = json as { schemaVersion?: unknown; items?: Raw[] } | null;
  if (!Array.isArray(doc?.items)) throw new Error('cleanup.json has no "items" array');
  if (typeof doc.schemaVersion === 'number' && doc.schemaVersion > 1) warn('schema.version', String(doc.schemaVersion));
  const seen = new Map<string, string>();
  return doc.items.map((r, order) => {
    if (!r || typeof r !== 'object' || typeof r.id !== 'string' || !r.id) throw new Error(`cleanup.json entry #${order} has no id`);
    const id = r.id;
    const prior = seen.get(id.toLowerCase());
    if (prior !== undefined) {
      throw new Error(
        prior === id
          ? `cleanup.json: duplicate id ${id}`
          : `cleanup.json: ids ${prior} and ${id} differ only by case`,
      );
    }
    seen.set(id.toLowerCase(), id);
    const text = {} as Record<'en' | 'pt-BR', ItemText>;
    for (const lang of LANGS) {
      const t = r.text?.[lang];
      for (const field of ['title', 'what', 'breaks'] as const) {
        if (typeof t?.[field] !== 'string' || !t[field]) {
          throw new Error(`cleanup.json: item ${id} has no ${field} in ${lang}`);
        }
      }
      const { gain, ...rest } = t as Record<string, unknown>;
      text[lang] = { ...rest, title: t!.title, what: t!.what, breaks: t!.breaks, ...(typeof gain === 'string' ? { gain } : {}) } as ItemText;
    }
    const actions: CleanupAction[] = [];
    for (const a of Array.isArray(r.actions) ? r.actions : []) {
      if (!a || typeof a !== 'object' || Array.isArray(a)) {
        warn('action.invalid', id);
        continue;
      }
      const type = typeof a.type === 'string' ? a.type : '';
      if (!KNOWN_ACTIONS.includes(type)) warn('action.unknown', `${id}: ${type}`);
      actions.push({ ...a, type });
    }
    return {
      ...r,
      id,
      category: str(r.category),
      level: str(r.level),
      scope: str(r.scope),
      contested: r.contested === true,
      requiresReboot: r.requiresReboot === true,
      laptopSensitive: r.laptopSensitive === true,
      optIn: r.optIn === true,
      text,
      actions,
      evidence: parseEvidence(id, r.evidence),
      order,
    } as CleanupItem;
  });
}

/** Known categories first (in KNOWN_CATEGORIES order), unknown ones by first appearance; items by level rank then file order. */
export function sortForIndex(items: CleanupItem[]): Map<string, CleanupItem[]> {
  const groups = new Map<string, CleanupItem[]>();
  for (const i of items) (groups.get(i.category) ?? groups.set(i.category, []).get(i.category)!).push(i);
  const known = (KNOWN_CATEGORIES as readonly string[]).filter((c) => groups.has(c));
  const unknown = [...groups.keys()].filter((c) => !(KNOWN_CATEGORIES as readonly string[]).includes(c));
  return new Map(
    [...known, ...unknown].map((c) => [c, groups.get(c)!.sort((a, b) => levelRank(a.level) - levelRank(b.level) || a.order - b.order)]),
  );
}

export function cleanupLoader(): Loader {
  return {
    name: 'fk-cleanup',
    async load({ store, parseData }) {
      const items = loadCleanup(JSON.parse(await readFile(FILE, 'utf8')));
      store.clear();
      for (const i of items) store.set({ id: i.id, data: await parseData({ id: i.id, data: i }) });
    },
  };
}
