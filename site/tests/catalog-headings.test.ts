import { expect, test } from 'vitest';
import { readFileSync } from 'node:fs';
import { catalogHeadings, itemHeadings, withHeadings } from '../src/lib/catalogHeadings';
import { loadCleanup, sortForIndex } from '../src/loaders/cleanup';
import { labelsFor, t } from '../src/lib/text';

const items = loadCleanup(JSON.parse(readFileSync('../src/FormatKit.Cleanup/Catalog/Data/cleanup.json', 'utf8')));

test('item headings have stable slugs and localized text', () => {
  const pt = itemHeadings((k) => t('pt-BR', k));
  const en = itemHeadings((k) => t('en', k));
  expect(pt.map((h) => h.slug)).toEqual(['what', 'breaks', 'when', 'evidence', 'undo', 'technical']);
  expect(en.map((h) => h.slug)).toEqual(pt.map((h) => h.slug));
  expect(en[0]).toEqual({ depth: 2, slug: 'what', text: 'What it does' });
  expect(pt[0].text).toBe('O que faz');
});

test('catalog headings follow the index order, one per category, slug is the category id', () => {
  const headings = catalogHeadings(items, labelsFor('en'));
  expect(headings.map((h) => h.slug)).toEqual([...sortForIndex(items).keys()]);
  expect(headings.every((h) => h.depth === 2)).toBe(true);
  expect(headings.find((h) => h.slug === 'apps')?.text).toBe('Apps');
});

test('withHeadings appends after the overview entry without touching it', () => {
  const overview = { depth: 2, slug: '_top', text: 'Overview', children: [] };
  const toc = [overview];
  const result = withHeadings(toc, [{ depth: 2, slug: 'a', text: 'A' }]);
  expect(result).toEqual([overview, { depth: 2, slug: 'a', text: 'A', children: [] }]);
  expect(toc).toHaveLength(1);
});
