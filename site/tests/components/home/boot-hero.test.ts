import { expect, test, vi } from 'vitest';
import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import BootHero from '../../../src/components/home/BootHero.astro';

vi.stubEnv('BASE_URL', '/formatkit/');

async function render(props: Record<string, unknown>, slots?: Record<string, string>) {
  const c = await AstroContainer.create();
  return c.renderToString(BootHero, { props: { title: 'T', subtitle: 'S', lead: 'L', ...props }, slots });
}

test('en: links, prompt and default log', async () => {
  const html = await render({ locale: 'en' });
  expect(html).toContain('href="/formatkit/en/download/"');
  expect(html).toContain('/formatkit/en/guide/before/');
  expect(html).toContain('/formatkit/en/catalog/');
  expect(html).toMatch(/<code[^>]*>formatkit run --profile cleanup-balanced\.json --dry-run<\/code>/);
  expect(html).toContain('Hardware detected');
  expect(html.match(/\[ OK \]/g)?.length).toBe(4);
  expect(html.match(/\[ \.\. \]/g)?.length).toBe(1);
});

test('pt-BR links and text', async () => {
  const html = await render({ locale: 'pt-BR' });
  expect(html).toContain('href="/formatkit/download/"');
  expect(html).toContain('/formatkit/guide/before/');
  expect(html).toContain('Hardware detectado');
});

test('custom logLines replace defaults', async () => {
  const html = await render({ locale: 'en', logLines: [{ status: 'ok', text: 'Custom line' }] });
  expect(html).toContain('Custom line');
  expect(html).not.toContain('Hardware detected');
  expect(html.match(/\[ OK \]/g)?.length).toBe(1);
});

test('motion button hidden and script reads fk-motion in try', async () => {
  const html = await render({ locale: 'en' });
  expect(html).toMatch(/<button[^>]*class="fk-motion[^"]*"[^>]*hidden[^>]*>\s*Pause motion\s*<\/button>/);
  expect(html).toMatch(/try\s*\{[^}]*fk-motion/);
});

test('warn slot rendered', async () => {
  const html = await render({ locale: 'en' }, { warn: '<span>WARNSLOT</span>' });
  expect(html).toContain('WARNSLOT');
});

test('motion-off rules do not force opacity on the texture or on all hero children', async () => {
  const { readFileSync } = await import('node:fs');
  const src = readFileSync(new URL('../../../src/components/home/BootHero.astro', import.meta.url), 'utf8');
  const rules = [...src.matchAll(/([^{}]+)\{([^{}]*)\}/g)].filter(
    (m) => /prefers-reduced-motion|data-motion|\.fk-hero \*|\.fk-texture|\.fk-log-lines li/.test(m[1]) && /opacity/.test(m[2]),
  );
  expect(rules.length).toBeGreaterThan(0);
  for (const r of rules) {
    expect(r[1]).not.toMatch(/\.fk-texture|\.fk-hero \*/);
    expect(r[1]).toMatch(/\.fk-log-lines li/);
  }
});

test('warn line sits below the hero grid, outside the log box', async () => {
  const html = await render({ locale: 'en' }, { warn: '<span>WARNSLOT</span>' });
  const warn = html.indexOf('WARNSLOT');
  expect(warn).toBeGreaterThan(html.indexOf('fk-hero-grid'));
  expect(html.slice(html.indexOf('fk-log-lines'), html.indexOf('</ul>'))).not.toContain('WARNSLOT');
  expect(html).toMatch(/<p class="fk-warn[^"]*"[^>]*>\s*<span>WARNSLOT/);
});

test('guide link text has no arrow of its own', async () => {
  const html = await render({ locale: 'en' });
  expect(html).not.toContain('↓');
});
