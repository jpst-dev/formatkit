import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { expect, test } from 'vitest';
import { loadPrograms } from '../src/loaders/programs';

const real = JSON.parse(
  readFileSync(fileURLToPath(new URL('../../src/FormatKit.Programs/Catalog/Data/programs.json', import.meta.url)), 'utf8'),
);

test('loads the real programs.json: non-empty, unique ids, both languages', () => {
  const list = loadPrograms(real);
  expect(list.length).toBeGreaterThan(0);
  expect(new Set(list.map((p) => p.id)).size).toBe(list.length);
  for (const p of list) {
    expect(p.text.en.name).toBeTruthy();
    expect(p.text['pt-BR'].name).toBeTruthy();
  }
});

test('missing pt-BR throws with the id', () => {
  const json = { programs: [{ id: 'x', category: 'c', source: 'winget', text: { en: { name: 'X', description: '' } } }] };
  expect(() => loadPrograms(json)).toThrow(/x.*pt-BR/);
});

test('missing id throws', () => {
  const json = { programs: [{ category: 'c', source: 'winget', text: {} }] };
  expect(() => loadPrograms(json)).toThrow(/id/);
});

test('duplicate id throws', () => {
  const p = { id: 'x', category: 'c', source: 'winget', text: { en: { name: 'X' }, 'pt-BR': { name: 'X' } } };
  expect(() => loadPrograms({ programs: [p, p] })).toThrow(/x/);
});

test('extra fields pass through and description may be empty', () => {
  const p = { id: 'x', category: 'c', source: 'winget', scope: 'machine', text: { en: { name: 'X' }, 'pt-BR': { name: 'X' } } };
  const [out] = loadPrograms({ programs: [p] });
  expect((out as Record<string, unknown>).scope).toBe('machine');
  expect(out.text.en.description).toBe('');
});
