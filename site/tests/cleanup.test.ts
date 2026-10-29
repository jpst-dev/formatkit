import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { beforeEach, expect, test } from 'vitest';
import { loadCleanup, sortForIndex } from '../src/loaders/cleanup';
import { readWarnings, resetWarnings } from '../src/lib/warnings';

const read = (url: string) => JSON.parse(readFileSync(fileURLToPath(new URL(url, import.meta.url)), 'utf8'));
const real = read('../../src/FormatKit.Cleanup/Catalog/Data/cleanup.json');
const fixture = (n: string) => read(`./fixtures/cleanup/${n}`);
const item = (over: Record<string, unknown> = {}) => ({
  id: 'a.b',
  category: 'apps',
  level: 'safe',
  scope: 'machine',
  text: {
    en: { title: 'T', what: 'W', breaks: 'B' },
    'pt-BR': { title: 'Tp', what: 'Wp', breaks: 'Bp' },
  },
  actions: [],
  ...over,
});
const doc = (...items: unknown[]) => ({ schemaVersion: 1, items });

beforeEach(() => resetWarnings());

test('loads the real cleanup.json', () => {
  const list = loadCleanup(real);
  expect(list.length).toBeGreaterThanOrEqual(34);
  expect(new Set(list.map((i) => i.id)).size).toBe(list.length);
  for (const i of list) expect(i.text['pt-BR'].title).toBeTruthy();
});

test('future.json loads and warns', () => {
  const list = loadCleanup(fixture('future.json'));
  expect(list[0].category).toBe('windowsSettings');
  expect(list[0].level).toBe('extreme');
  expect(list[0].actions[0]).toEqual({ type: 'service', name: 'DiagTrack' });
  const codes = readWarnings().map((w) => w.code);
  expect(codes).toContain('schema.version');
  expect(codes).toContain('action.unknown');
});

test('booleans default to false, order is file position', () => {
  const [a, b] = loadCleanup(doc(item({ id: 'a.1' }), item({ id: 'a.2', contested: true })));
  expect(a).toMatchObject({ contested: false, requiresReboot: false, laptopSensitive: false, order: 0 });
  expect(b).toMatchObject({ contested: true, order: 1 });
});

test('evidence shapes', () => {
  expect(loadCleanup(doc(item({ evidence: 'strong' })))[0].evidence).toEqual({ level: 'strong', sources: [] });
  const ev = { level: 'mixed', sources: [{ title: 'T', url: 'https://e' }] };
  expect(loadCleanup(doc(item({ evidence: ev })))[0].evidence).toEqual(ev);
  expect(readWarnings()).toEqual([]);
  expect(loadCleanup(doc(item()))[0].evidence).toBeNull();
  expect(readWarnings()).toEqual([]);
  expect(loadCleanup(doc(item({ evidence: 42 })))[0].evidence).toBeNull();
  expect(readWarnings()).toContainEqual({ code: 'evidence.shape', detail: 'a.b' });
});

test('hard errors', () => {
  expect(() => loadCleanup(fixture('missing-breaks.json'))).toThrow(/apps\.x.*breaks.*pt-BR/);
  expect(() => loadCleanup(fixture('duplicate-id.json'))).toThrow(/duplicate id apps\.x/);
  expect(() => loadCleanup(fixture('case-collision.json'))).toThrow(/differ only by case/);
  expect(() => loadCleanup(doc(item({ id: '' })))).toThrow(/id/);
});

test('sortForIndex orders categories and levels', () => {
  const list = loadCleanup(
    doc(
      item({ id: 'w.1', category: 'windowsSettings' }),
      item({ id: 'a.2', level: 'balanced' }),
      item({ id: 'a.3', level: 'extreme' }),
      item({ id: 'a.1', level: 'safe' }),
    ),
  );
  const sorted = sortForIndex(list);
  expect([...sorted.keys()]).toEqual(['apps', 'windowsSettings']);
  expect(sorted.get('apps')!.map((i) => i.id)).toEqual(['a.1', 'a.2', 'a.3']);
});

test('malformed actions are tolerated', () => {
  const [i] = loadCleanup(doc(item({ actions: [null, {}, { type: 5 }, 'x', { type: 'registry' }] })));
  expect(i.actions).toEqual([{}, {}, { type: 'registry' }].map((a, n) => (n < 2 ? { type: '' } : a)));
  const w = readWarnings();
  expect(w).toContainEqual({ code: 'action.invalid', detail: 'a.b' });
  expect(w).toContainEqual({ code: 'action.unknown', detail: 'a.b: ' });
});

test('non-object item entry throws the no-id message', () => {
  expect(() => loadCleanup(doc(null))).toThrow(/entry #0 has no id/);
  expect(() => loadCleanup(doc('x'))).toThrow(/entry #0 has no id/);
});

test('non-string gain is dropped', () => {
  const t = { en: { title: 'T', what: 'W', breaks: 'B', gain: 5 }, 'pt-BR': { title: 'Tp', what: 'Wp', breaks: 'Bp', gain: 'G' } };
  const [i] = loadCleanup(doc(item({ text: t })));
  expect('gain' in i.text.en).toBe(false);
  expect(i.text['pt-BR'].gain).toBe('G');
});
