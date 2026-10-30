import { expect, test } from 'vitest';
import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import CatalogStat from '../../../src/components/home/CatalogStat.astro';
import CatalogTexture from '../../../src/components/home/CatalogTexture.astro';
import HomeSection from '../../../src/components/home/HomeSection.astro';
import type { CleanupItem } from '../../../src/loaders/cleanup';

function mk(id: string, category: string, order: number): CleanupItem {
  return {
    id,
    category,
    level: 'safe',
    scope: 'user',
    contested: false,
    requiresReboot: false,
    laptopSensitive: false,
    optIn: false,
    text: { en: { title: id, what: 'w', breaks: 'b' }, 'pt-BR': { title: id, what: 'w', breaks: 'b' } },
    actions: [],
    evidence: null,
    order,
  } as CleanupItem;
}
const items = [
  mk('gaming.a', 'gaming', 0),
  mk('apps.a', 'apps', 1),
  mk('apps.b', 'apps', 2),
  mk('privacy.a', 'privacy', 3),
  mk('developer.a', 'developer', 4),
];

async function render(Comp: any, props: Record<string, unknown>, slots?: Record<string, string>) {
  const c = await AstroContainer.create();
  return c.renderToString(Comp, { props, slots });
}

test('CatalogStat en: total, ordered categories, counts, label', async () => {
  const html = await render(CatalogStat, { items, locale: 'en' });
  expect(html).toMatch(/fk-stat-total[^>]*>\s*5\s*</);
  const pos = ['Apps', 'Privacy', 'Developer', 'Gaming'].map((l) => html.indexOf(l));
  expect(pos.every((p) => p > -1)).toBe(true);
  expect([...pos].sort((a, b) => a - b)).toEqual(pos);
  expect(html).toMatch(/fk-stat-cat/);
  expect(html).toContain('tweaks in the catalog');
  const counts = [...html.matchAll(/fk-stat-count[^>]*>\s*(\d+)\s*</g)].map((m) => m[1]);
  expect(counts).toEqual(['2', '1', '1', '1']);
});

test('CatalogStat pt-BR', async () => {
  const html = await render(CatalogStat, { items, locale: 'pt-BR' });
  expect(html).toContain('Privacidade');
  expect(html).toContain('ajustes no catálogo');
});

test('CatalogTexture shows ids, aria-hidden', async () => {
  const html = await render(CatalogTexture, { ids: ['apps.a', 'privacy.b'] });
  expect(html).toContain('aria-hidden="true"');
  expect(html).toContain('apps.a');
  expect(html).toContain('privacy.b');
});

test('CatalogTexture with no ids renders empty texture', async () => {
  const html = await render(CatalogTexture, { ids: [] });
  expect(html).toContain('fk-texture');
  expect(html).not.toMatch(/[a-z]+\.[a-z]+/);
});

test('HomeSection renders number, label and slot', async () => {
  const html = await render(HomeSection, { num: '03', label: 'O catálogo' }, { default: '<p>x</p>' });
  expect(html).toContain('03');
  expect(html).toContain('O catálogo');
  expect(html).toContain('<p>x</p>');
});
