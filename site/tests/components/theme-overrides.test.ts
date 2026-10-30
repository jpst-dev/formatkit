import { expect, test } from 'vitest';
import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import ThemeProvider from '../../src/components/overrides/ThemeProvider.astro';
import ThemeSelect from '../../src/components/overrides/ThemeSelect.astro';

async function render(component: Parameters<AstroContainer['renderToString']>[0]) {
  const c = await AstroContainer.create();
  return c.renderToString(component);
}

test('ThemeSelect renders no theme picker', async () => {
  const html = await render(ThemeSelect);
  expect(html).not.toMatch(/<select|starlight-theme-select/);
});

test('ThemeProvider forces dark and keeps the Starlight stub', async () => {
  const html = await render(ThemeProvider);
  expect(html).toMatch(/dataset\.theme = 'dark'|data-theme="dark"/);
  expect(html).toContain('StarlightThemeProvider');
  expect(html).toContain('updatePickers');
  expect(html).not.toContain('prefers-color-scheme');
});
