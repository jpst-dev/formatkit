import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { expect, test } from 'vitest';
import { loadKnownIssues } from '../src/loaders/knownIssues';

const real = JSON.parse(
  readFileSync(fileURLToPath(new URL('../../src/FormatKit.Cleanup/Catalog/Data/known-issues.json', import.meta.url)), 'utf8'),
);
const issue = (over: Record<string, unknown> = {}) => ({
  id: 'a.b',
  severity: 'breaks',
  text: {
    en: { title: 'T', breaks: 'B', fix: 'F' },
    'pt-BR': { title: 'Tp', breaks: 'Bp', fix: 'F' },
  },
  ...over,
});

test('loads the real known-issues.json: non-empty, unique ids, both languages', () => {
  const list = loadKnownIssues(real);
  expect(list.length).toBeGreaterThan(0);
  expect(new Set(list.map((i) => i.id)).size).toBe(list.length);
  for (const i of list) {
    expect(i.text.en.title).toBeTruthy();
    expect(i.text['pt-BR'].breaks).toBeTruthy();
  }
});

test('extra fields pass through', () => {
  const [i] = loadKnownIssues({ issues: [issue({ rule: { kind: 'x' } })] });
  expect((i as Record<string, unknown>).rule).toEqual({ kind: 'x' });
});

test('missing id throws', () => {
  expect(() => loadKnownIssues({ issues: [issue({ id: undefined })] })).toThrow(/id/);
});

test('duplicate id throws, also when only the case differs', () => {
  expect(() => loadKnownIssues({ issues: [issue(), issue()] })).toThrow(/a\.b/);
  expect(() => loadKnownIssues({ issues: [issue(), issue({ id: 'A.B' })] })).toThrow(/A\.B/);
});

test('missing pt-BR breaks throws with the id', () => {
  const bad = issue({ text: { en: { title: 'T', breaks: 'B', fix: '' }, 'pt-BR': { title: 'x', fix: '' } } });
  expect(() => loadKnownIssues({ issues: [bad] })).toThrow(/a\.b.*pt-BR/);
});

test('missing en title throws', () => {
  const bad = issue({ text: { 'pt-BR': { title: 'x', breaks: 'y', fix: '' } } });
  expect(() => loadKnownIssues({ issues: [bad] })).toThrow(/a\.b.*en/);
});
