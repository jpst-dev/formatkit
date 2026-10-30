import { expect, test } from 'vitest';
import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import ProgramList from '../../src/components/ProgramList.astro';
import type { Program } from '../../src/loaders/programs';

const programs: Program[] = [
  { id: 'browsers.a', category: 'browsers', source: 'winget', wingetId: 'A.A',
    text: { en: { name: 'Alpha <b>', description: 'A browser.' }, 'pt-BR': { name: 'Alfa', description: 'Um navegador.' } } },
  { id: 'media.b', category: 'media', source: 'msstore', wingetId: 'B',
    text: { en: { name: 'Beta', description: 'A player.' }, 'pt-BR': { name: 'Beta', description: 'Um player.' } } },
];

async function render(props: Record<string, unknown>) {
  const c = await AstroContainer.create();
  return c.renderToString(ProgramList, { props });
}

test('groups by category label and shows name, description, source', async () => {
  const html = await render({ programs, locale: 'en' });
  expect(html).toContain('Browsers');
  expect(html).toContain('Media');
  expect(html).toContain('A browser.');
  expect(html).toContain('Microsoft Store');
  expect(html.indexOf('Browsers')).toBeLessThan(html.indexOf('Alpha'));
});

test('pt-BR uses Portuguese names and category labels', async () => {
  const html = await render({ programs, locale: 'pt-BR' });
  expect(html).toContain('Navegadores');
  expect(html).toContain('Um navegador.');
});

test('escapes < in names', async () => {
  const html = await render({ programs, locale: 'en' });
  expect(html).toContain('Alpha &lt;b&gt;');
});
