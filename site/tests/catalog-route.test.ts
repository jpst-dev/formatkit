import { expect, test } from 'vitest';
import { readFileSync } from 'node:fs';
import { pathsFor } from '../src/lib/catalogPaths';
import { loadCleanup } from '../src/loaders/cleanup';

const items = loadCleanup(JSON.parse(readFileSync('../src/FormatKit.Cleanup/Catalog/Data/cleanup.json', 'utf8')));

test('two paths per item, root locale undefined and en', () => {
  const paths = pathsFor(items);
  expect(paths).toHaveLength(items.length * 2);
  expect(paths.map((p) => p.params)).toContainEqual({ locale: 'en', id: 'apps.clipchamp' });
  expect(paths.map((p) => p.params)).toContainEqual({ locale: undefined, id: 'apps.clipchamp' });
  expect(paths[0].props.item.id).toBe(paths[0].params.id);
});
