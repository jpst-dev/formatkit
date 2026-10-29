import { expect, test } from 'vitest';
import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import ReleaseNotes from '../../src/components/ReleaseNotes.astro';
import type { Release } from '../../src/loaders/releases';

const mk = (tag: string, body: string): Release => ({
  id: tag, tag, name: tag, date: '2026-10-10T10:00:00Z',
  url: `https://github.com/jpst-dev/formatkit/releases/tag/${tag}`, prerelease: false, body, assets: [],
});

async function render(props: Record<string, unknown>) {
  const c = await AstroContainer.create();
  return c.renderToString(ReleaseNotes, { props });
}

test('empty body shows the no-notes string, never null', async () => {
  const html = await render({ releases: [mk('v1.0.0', '')], locale: 'en' });
  expect(html).toContain('No release notes.');
  expect(html).not.toContain('null');
});

test('renders markdown notes with raw HTML escaped, newest first', async () => {
  const html = await render({
    releases: [mk('v2.0.0', '**new** <script>x</script>'), mk('v1.0.0', 'old')],
    locale: 'pt-BR',
  });
  expect(html).toContain('<strong>new</strong>');
  expect(html).toContain('&lt;script&gt;');
  expect(html.indexOf('v2.0.0')).toBeLessThan(html.indexOf('v1.0.0'));
  expect(html).toContain('github.com/jpst-dev/formatkit/releases/tag/v2.0.0');
});

test('no releases shows the empty string', async () => {
  const html = await render({ releases: [], locale: 'en' });
  expect(html).toContain('No releases published yet');
});

test('notes are wrapped in lang="en" and headings sit one level below the release heading', async () => {
  const html = await render({ releases: [mk('v1.0.0', '## Changes\n\n# Top\n\ntext')], locale: 'pt-BR' });
  expect(html).toContain('<div lang="en">');
  expect(html).toContain('<h3>Changes</h3>');
  expect(html).toContain('<h3>Top</h3>');
  expect(html).not.toContain('<h1>');
});
