import { expect, test } from 'vitest';
import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import KnownIssues from '../../src/components/KnownIssues.astro';
import type { KnownIssue } from '../../src/loaders/knownIssues';

const issues: KnownIssue[] = [
  {
    id: 'service.bits',
    severity: 'breaks',
    text: {
      en: { title: 'BITS is off', breaks: 'Downloads <fail> & stall.', fix: 'sc.exe config BITS start= demand' },
      'pt-BR': { title: 'O BITS está desligado', breaks: 'Downloads falham.', fix: 'sc.exe config BITS start= demand' },
    },
  },
  {
    id: 'security.vbs-off',
    severity: 'security',
    text: {
      en: { title: 'VBS is off', breaks: 'Less protection.', fix: 'Turn it on in Windows Security.' },
      'pt-BR': { title: 'VBS desligado', breaks: 'Menos proteção.', fix: 'Ative em Segurança do Windows.' },
    },
  },
];

async function render(props: Record<string, unknown>) {
  const c = await AstroContainer.create();
  return c.renderToString(KnownIssues, { props });
}

test('renders the locale title, what breaks and the fix as code', async () => {
  const html = await render({ issues, locale: 'en' });
  expect(html).toContain('BITS is off');
  expect(html).toMatch(/<code[^>]*>sc\.exe config BITS start= demand<\/code>/);
});

test('pt-BR renders Portuguese text and group headings', async () => {
  const html = await render({ issues, locale: 'pt-BR' });
  expect(html).toContain('O BITS está desligado');
  expect(html).toContain('Quebra recursos do Windows');
  expect(html).toContain('Riscos de segurança');
});

test('escapes < and & in text', async () => {
  const html = await render({ issues, locale: 'en' });
  expect(html).toContain('Downloads &lt;fail&gt; &amp; stall.');
  expect(html).not.toContain('<fail>');
});

test('a fix that is prose is not shown as code', async () => {
  const html = await render({ issues, locale: 'en' });
  expect(html).not.toMatch(/<code[^>]*>Turn it on/);
  expect(html).toContain('Turn it on in Windows Security.');
});
