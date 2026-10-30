import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightLinksValidator from 'starlight-links-validator';
import { fkWarnings } from './src/integrations/warnings';

export default defineConfig({
  site: 'https://jpst-dev.github.io',
  base: '/formatkit',
  integrations: [
    starlight({
      title: 'FormatKit',
      // Same mark as the exe icon (assets/formatkit.svg, copied by tools/make-icon).
      favicon: '/favicon.svg',
      defaultLocale: 'root',
      locales: {
        root: { label: 'Português', lang: 'pt-BR' },
        en: { label: 'English', lang: 'en' },
      },
      // Pages are added to the sidebar by the task that writes them.
      sidebar: [
        { label: 'Download', translations: { 'pt-BR': 'Baixar' }, slug: 'download' },
        {
          label: 'Guide',
          translations: { 'pt-BR': 'Guia' },
          items: [
            { slug: 'guide/before' },
            { slug: 'guide/media' },
            { slug: 'guide/install' },
            { slug: 'guide/profile' },
            { slug: 'guide/after' },
          ],
        },
        { label: 'Catalog', translations: { 'pt-BR': 'Catálogo' }, slug: 'catalog' },
        { label: 'Safety & undo', translations: { 'pt-BR': 'Segurança e desfazer' }, slug: 'safety' },
        { label: 'Recommendations', translations: { 'pt-BR': 'Recomendações' }, slug: 'recommendations' },
        { label: 'FAQ', translations: { 'pt-BR': 'Perguntas frequentes' }, slug: 'faq' },
        { label: 'Changelog', translations: { 'pt-BR': 'Novidades' }, slug: 'changelog' },
      ],
      customCss: ['./src/styles/theme.css'],
      components: {
        ThemeProvider: './src/components/overrides/ThemeProvider.astro',
        ThemeSelect: './src/components/overrides/ThemeSelect.astro',
        Hero: './src/components/overrides/Hero.astro',
      },
      expressiveCode: {
        themes: ['github-dark'],
        // github-dark supplies green-ish strings and dim comments; panel colours come from the tokens.
        styleOverrides: {
          codeBackground: 'var(--fk-panel)',
          borderColor: 'var(--fk-line)',
          codeFontFamily: 'var(--fk-mono)',
          uiFontFamily: 'var(--fk-mono)',
        },
      },
      plugins: [starlightLinksValidator()],
    }),
    fkWarnings(),
  ],
});
