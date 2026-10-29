import { expect, test } from 'vitest';
import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import CatalogItem from '../../src/components/CatalogItem.astro';
import CatalogIndex from '../../src/components/CatalogIndex.astro';
import ActionDetails from '../../src/components/ActionDetails.astro';
import type { CleanupItem } from '../../src/loaders/cleanup';

const item: CleanupItem = {
  id: 'apps.x',
  category: 'apps',
  level: 'balanced',
  scope: 'machine',
  contested: true,
  requiresReboot: true,
  laptopSensitive: true,
  text: {
    en: { title: 'Remove X', what: 'Use <b> & HKLM:\SOFTWARE\X', breaks: 'Nothing & more', gain: 'Frees space' },
    'pt-BR': { title: 'Remover X', what: 'Usa <b> & HKLM:\SOFTWARE\X', breaks: 'Nada' },
  },
  actions: [{ type: 'appx', package: 'P.X', storeId: 'S1' }],
  evidence: null,
  order: 0,
};

async function render(Comp: any, props: Record<string, unknown>) {
  const c = await AstroContainer.create();
  return c.renderToString(Comp, { props });
}

test('pt-BR item shows the section headings, undo command and not-stated evidence', async () => {
  const html = await render(CatalogItem, { item, locale: 'pt-BR' });
  for (const h of ['O que faz', 'O que pode quebrar', 'Quando usar', 'Evidência', 'Como desfazer', 'Detalhes técnicos']) {
    expect(html).toContain(h);
  }
  expect(html).toContain('formatkit cleanup undo apps.x');
  expect(html).toContain('Ainda não informada');
});

test('en item shows gain and english headings', async () => {
  const html = await render(CatalogItem, { item, locale: 'en' });
  expect(html).toContain('What it does');
  expect(html).toContain('Frees space');
  expect(html).toContain('Not stated yet');
});

test('catalog text is escaped and backslashes kept', async () => {
  const html = await render(CatalogItem, { item, locale: 'en' });
  expect(html).toContain('Use &lt;b&gt; &amp; HKLM:\SOFTWARE\X');
  expect(html).not.toContain('<b>');
});

test('evidence level and sources render', async () => {
  const html = await render(CatalogItem, {
    item: { ...item, evidence: { level: 'documented', sources: [{ title: 'MS Docs', url: 'https://example.com/a' }] } },
    locale: 'en',
  });
  expect(html).toContain('documented');
  expect(html).toContain('MS Docs');
  expect(html).toContain('href="https://example.com/a"');
});

test('ActionDetails renders a key/value list', async () => {
  const html = await render(ActionDetails, { action: { type: 'service', name: 'DiagTrack' } });
  expect(html).toContain('<dl');
  expect(html).toContain('type');
  expect(html).toContain('service');
  expect(html).toContain('name');
  expect(html).toContain('DiagTrack');
});

test('index groups by category and links with the base', async () => {
  const html = await render(CatalogIndex, { items: [item], locale: 'en' });
  expect(html).toMatch(/href="(\/formatkit)?\/en\/catalog\/apps\.x\/"/);
  expect(html).toContain('Remove X');
  const pt = await render(CatalogIndex, { items: [item], locale: 'pt-BR' });
  expect(pt).toMatch(/href="(\/formatkit)?\/catalog\/apps\.x\/"/);
});
