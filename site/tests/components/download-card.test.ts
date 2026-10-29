import { expect, test } from 'vitest';
import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import DownloadCard from '../../src/components/DownloadCard.astro';
import type { Release } from '../../src/loaders/releases';

const H1 = 'a'.repeat(64);
const H2 = 'b'.repeat(64);
const release: Release = {
  id: 'v1.0.0', tag: 'v1.0.0', name: 'v1.0.0', date: '2026-10-10T10:00:00Z',
  url: 'https://github.com/jpst-dev/formatkit/releases/tag/v1.0.0', prerelease: false, body: '',
  assets: [
    { name: 'FormatKitUI.exe', url: 'https://example.test/FormatKitUI.exe', size: 5242880, sha256: H1 },
    { name: 'formatkit.exe', url: 'https://example.test/formatkit.exe', size: 2097152, sha256: H2 },
  ],
};

async function render(props: Record<string, unknown>) {
  const c = await AstroContainer.create();
  return c.renderToString(DownloadCard, { props });
}

test('renders both files, hashes, Get-FileHash command and SmartScreen note', async () => {
  const html = await render({ release, locale: 'en' });
  expect(html).toContain('FormatKitUI.exe');
  expect(html).toContain('formatkit.exe');
  expect(html).toContain(H1);
  expect(html).toContain(H2);
  expect(html).toContain('Get-FileHash .\formatkit.exe -Algorithm SHA256');
  expect(html).toContain('SmartScreen');
  expect(html).toContain('v1.0.0');
});

test('pt-BR uses Portuguese strings', async () => {
  const html = await render({ release, locale: 'pt-BR' });
  expect(html).toContain('Executar assim mesmo');
});

test('null release shows the no-release text and the releases link', async () => {
  const html = await render({ release: null, locale: 'en' });
  expect(html).toContain('No release has been published yet');
  expect(html).toContain('https://github.com/jpst-dev/formatkit/releases');
  expect(html).not.toContain('Get-FileHash');
});

test('asset without a hash says so instead of printing null', async () => {
  const r = { ...release, assets: [{ ...release.assets[0], sha256: null }] };
  const html = await render({ release: r, locale: 'en' });
  expect(html).not.toContain('null');
  expect(html).toContain('SHA-256 not available');
});
