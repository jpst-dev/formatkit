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
  optIn: false,
  text: {
    en: { title: 'Remove X', what: String.raw`Use <b> & HKLM:\SOFTWARE\X`, breaks: 'Nothing & more', gain: 'Frees space' },
    'pt-BR': { title: 'Remover X', what: String.raw`Usa <b> & HKLM:\SOFTWARE\X`, breaks: 'Nada' },
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
  expect(html).toContain(String.raw`Use &lt;b&gt; &amp; HKLM:\SOFTWARE\X`);
  expect(html).toContain(String.raw`\SOFTWARE\X`);
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

test('evidence source links only for http(s) URLs', async () => {
  const withEvidence: CleanupItem = {
    ...item,
    evidence: {
      level: 'documented',
      sources: [
        { title: 'Good', url: 'https://example.test/a' },
        { title: 'Plain', url: 'javascript:alert(1)' },
      ],
    },
  } as CleanupItem;
  const html = await render(CatalogItem, { item: withEvidence, locale: 'en' });
  expect(html).toContain('href="https://example.test/a"');
  expect(html).toContain('Plain');
  expect(html).not.toContain('javascript:');
});

test('item without actions shows a line instead of an empty details', async () => {
  const html = await render(CatalogItem, { item: { ...item, actions: [] }, locale: 'en' });
  expect(html).toContain('No technical actions');
  expect(html).not.toContain('<details');
});

test('undo journal link points at the Undo section of the safety page', async () => {
  const en = await render(CatalogItem, { item, locale: 'en' });
  expect(en).toContain('/safety/#undo"');
  const pt = await render(CatalogItem, { item, locale: 'pt-BR' });
  expect(pt).toContain('/safety/#desfazer"');
});

test('item page shows the id line first, before the badges', async () => {
  const html = await render(CatalogItem, { item, locale: 'en' });
  expect(html).toMatch(/<p class="fk-item-id[^"]*"[^>]*><code[^>]*>apps\.x<\/code><\/p>/);
  expect(html.indexOf('fk-item-id')).toBeLessThan(html.indexOf('fk-badges'));
});

test('item badges carry tones: safe green, aggressive amber, balanced plain, contested amber', async () => {
  const safe = await render(CatalogItem, { item: { ...item, level: 'safe', contested: false }, locale: 'en' });
  expect(safe).toMatch(/data-tone="green"/);
  const aggressive = await render(CatalogItem, { item: { ...item, level: 'aggressive', contested: false }, locale: 'en' });
  expect(aggressive).toMatch(/data-tone="amber"/);
  const balanced = await render(CatalogItem, { item: { ...item, level: 'balanced', contested: false }, locale: 'en' });
  expect(balanced).not.toMatch(/data-tone="(green|amber)"/);
  expect(balanced).toMatch(/data-tone="plain"/);
  const contested = await render(CatalogItem, { item: { ...item, level: 'balanced', contested: true }, locale: 'en' });
  expect(contested).toMatch(/data-tone="amber">Contested</);
});

test('index flags carry the same tones', async () => {
  for (const [level, tone] of [['safe', 'green'], ['aggressive', 'amber'], ['balanced', 'plain']] as const) {
    const html = await render(CatalogIndex, { items: [{ ...item, level, contested: false, laptopSensitive: false, requiresReboot: false }], locale: 'en' });
    expect(html).toMatch(new RegExp(`class="fk-badge[^"]*"[^>]*data-tone="${tone}"|data-tone="${tone}"[^>]*class="fk-badge`));
  }
});

test('opt-in items show an amber Opt-in badge on the item page and the index', async () => {
  const optIn = { ...item, contested: false, optIn: true };
  expect(await render(CatalogItem, { item: optIn, locale: 'en' })).toMatch(/data-tone="amber">Opt-in</);
  expect(await render(CatalogIndex, { items: [optIn], locale: 'pt-BR' })).toMatch(/data-tone="amber">Opcional</);
  expect(await render(CatalogItem, { item: { ...optIn, optIn: false }, locale: 'en' })).not.toMatch(/Opt-in</);
});
