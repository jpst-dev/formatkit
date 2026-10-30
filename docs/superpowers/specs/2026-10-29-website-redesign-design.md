# FormatKit website: visual redesign ("phosphor terminal")

Date: 2026-10-29. Builds on `docs/superpowers/specs/2026-10-29-website-design.md` (the website spec). That spec still governs content, data, i18n, checks and deployment; this one only changes how the site looks and how the home page is built. Where they disagree on looks, this one wins.

## 1. Purpose

The site works but looks like every default Starlight site. The owner wants a distinctive look, in the spirit of ponytail.dev (dark, monospace, big headlines with an italic grey second line, numbered sections, ASCII texture, one glowing primary button), without copying it. The identity comes from what FormatKit does: a fresh Windows install booting into a ready machine, shown as a terminal session.

### Success criteria

- The home page is recognisably "FormatKit": a phosphor-green terminal look, a running `[ OK ]` session in the hero, a texture made of real catalog ids, numbered sections.
- Docs pages share the look and stay easy to read: monospace for headings and navigation, a sans-serif for body text.
- Nothing from the website spec regresses: both locales, search, links validator, `check:dist` (no third-party hosts), all existing tests.
- Text meets WCAG 2.2 AA contrast; motion respects `prefers-reduced-motion` and can be paused.

## 2. Scope

In: a site-wide dark theme, fonts, Expressive Code colours, a new home page (both locales), restyled catalog item page and badges, restyled asides, header and footer.

Out: a light mode (dark only, decided); a measurements section on the home page (comes with website Tasks 12-13, when the data exists); a drawn logo or new favicon (the text mark `formatkit▮` is the brand); any change to content, data loaders or deployment.

## 3. Decisions

| # | Decision | Why |
|---|---|---|
| R1 | Direction "boot media", variant A2 "phosphor terminal" | Chosen by the owner from three mockups (setup blue, phosphor, BIOS) |
| R2 | Dark only; the theme selector is removed | Chosen by the owner; stronger identity, less to maintain |
| R3 | Keep Astro + Starlight; restyle through `--sl-*` variables, `customCss` and component overrides | Keeps search, i18n, sidebar, link validation and checks |
| R4 | Fonts JetBrains Mono (headings, nav, code, labels) and Inter (body), self-hosted via `@fontsource` packages | Both OFL; bundled into the build, so `check:dist` sees no third-party host |
| R5 | Home page stays `index.mdx` per locale on the `splash` template, without the `hero` frontmatter, built from new components | Copy stays in MDX per locale, as today; Starlight header and search stay |
| R6 | Catalog numbers and texture ids are read from the `cleanup` collection at build time | The home page follows the catalog with no manual edits |
| R7 | Motion is CSS only, off under `prefers-reduced-motion`, with a pause button that remembers the choice | WCAG 2.2.2: motion longer than 5 s needs a pause control |

## 4. Visual tokens

Defined once in `site/src/styles/theme.css` as custom properties, mapped onto Starlight's `--sl-color-*` variables:

| Token | Value | Use |
|---|---|---|
| `--fk-bg` | `#07090a` | page background |
| `--fk-panel` | `#0b100d` | code, log boxes, cards |
| `--fk-line` | `#1d2b22` | borders |
| `--fk-rule` | `#121a15` | section dividers |
| `--fk-text` | `#e6f5e9` | headings |
| `--fk-body` | `#c3d0c7` | body text |
| `--fk-dim` | `#7f8d83` | secondary text, italic sublines (5.7:1 on `--fk-bg`) |
| `--fk-green` | `#3dff8a` | accent, `[ OK ]`, links, active nav, primary button |
| `--fk-on-green` | `#03140a` | text on the green button |
| `--fk-amber` | `#ffcf5a` | `[ WARN ]`, caution asides |
| `--fk-amber-bg` | `#141006` | caution aside background |

Every text/background pair used must reach 4.5:1 (3:1 for text 24 px and up). The primary button has a green glow (`box-shadow`), pill radius; secondary actions are plain dim text with an arrow.

Type: headings JetBrains Mono 800, tight letter spacing; the home `h1` about 4.5 rem on desktop, 2.25 rem at phone width. Italic sublines in `--fk-dim`. Small labels are uppercase mono with wide tracking. Body text Inter 400, 1.65 line height.

## 5. Site-wide (all pages)

- `astro.config.mjs`: `customCss` loads the fonts and `theme.css`; `components` overrides `ThemeProvider` (always dark) and `ThemeSelect` (renders nothing); `expressiveCode` gets a dark theme matching the tokens (panel background, green for strings/keys, dim comments).
- Header: site title rendered as `formatkit` plus a green block cursor; translucent background with a blur; mono nav text.
- Sidebar: mono, active entry in green with a left bar.
- Content headings (`h2`, `h3` in docs): mono; `h2` gets a dim `## ` prefix through CSS only (not in the text, so anchors and search stay clean).
- Asides: `caution`/`danger` use amber, `note`/`tip` use green, both on `--fk-panel` with a left border.
- Footer: dim mono line.

## 6. Home page

Same structure in `index.mdx` and `en/index.mdx`; only the copy differs. Sections, top to bottom:

0. **Hero** (`BootHero`): small label (`FormatKit · Windows 11 x64 →`, links to the download page), `h1` "Windows limpo. Máquina pronta." / "Clean Windows. Ready machine.", italic subline "Com o mínimo de cliques." / "With as few clicks as possible.", one paragraph from today's tagline, primary button "Baixar" → download, secondary "Ler o guia ↓" → `guide/before/`, plus "Ver o catálogo" and "GitHub" as dim links. Beside it, a log box: the prompt `formatkit run --profile cleanup-balanced.json --dry-run` (a real README command, rendered in a `<code>` element; `boot-hero.test.ts` pins it, since `check:dist` only reads the MDX sources) and four or five lines that appear one after another, each `[ OK ]` or `[ .. ]` with a real step or catalog id, ending on a blinking cursor. Behind it, `CatalogTexture`. Below it, a `[ WARN ]` amber line with today's caution text ("Esta ferramenta altera o sistema…"): the warning stays on the home page, as the website spec requires.
1. **01 · Como funciona**: "Formatou. Rodou um perfil." / italic "Voltou a trabalhar."; four step cards: detectar, drivers, programas, limpeza.
2. **02 · O perfil**: "Um arquivo." / italic "Não uma imagem."; a short paragraph; the real contents of `profiles/cleanup-balanced.json` as a JSON code block.
3. **03 · O catálogo** (`CatalogStat`): the total item count as a huge outlined number with a scan-line fill, the per-category counts below it with their labels from i18n, a sentence and a link to the catalog.
4. **04 · Segurança**: "Tudo tem volta." / italic "E tudo fica no log."; two cards: `--dry-run` and the undo journal, linking to the safety page.
5. **05 · Dois jeitos**: `FormatKitUI.exe` (window) and `formatkit.exe` (command line), linking to download.
6. **Closing call**: "Pronto para formatar?", one line (Windows 11 x64, no .NET install, SHA-256 published per release), the primary button.

The English page mirrors this with English copy. Every link carries the `/formatkit` base. Hard-coded command names in code spans stay within the README table (checked by `check:dist`).

## 7. Components

All in `site/src/components/home/`, each taking optional props with the collection or i18n as fallback, like the existing components.

- `BootHero.astro`: props `locale?`, `logLines?: { status: 'ok' | 'run'; text: string }[]`. Default log lines come from i18n keys `home.log.prompt` and `home.log.1`…`home.log.5`. Renders the pause button (`home.motion.pause` / `home.motion.play`) that toggles `data-motion="off"` on `<html>` and stores it in `localStorage` under `fk-motion` (read in a small inline script, wrapped in `try/catch`).
- `CatalogTexture.astro`: props `ids?: string[]` (default: every id in the `cleanup` collection, sorted). Renders the ids repeated to fill the box, `aria-hidden="true"`, low opacity, a slow vertical drift animation.
- `HomeSection.astro`: props `num: string`, `label: string`; slot for content. Renders the number and label column and the content column; one column at phone width.
- `CatalogStat.astro`: props `locale?`, `items?: CleanupItem[]` (default: the collection). Renders the total and per-category counts in `KNOWN_CATEGORIES` order, with labels through `labelFor`; unknown categories are counted under their raw name, as the catalog page does.
- New i18n keys (both files, identical keys): `home.log.prompt`, `home.log.1`…`home.log.5`, `home.motion.pause`, `home.motion.play`, `home.stat.items`.

Catalog item page: `CatalogItem.astro` shows the id in mono green under the title; badges become square mono tags, the level badge filled with a green dot when `safe`, amber when `aggressive`, plain otherwise; the "contested" badge is amber. No change to its sections or logic.

## 8. Motion

- Hero log lines fade in one after another (CSS `animation-delay`), the cursor blinks, the texture drifts slowly. Total entry animation under 3 s; drift and blink loop.
- `@media (prefers-reduced-motion: reduce)` and `html[data-motion="off"]` both stop every animation and show the final state (all lines visible, cursor solid).
- No JavaScript is needed for the page to read correctly; with JS off, motion runs (or not, per the media query) and the pause button is hidden.

## 9. Testing and checks

- Container API tests in `site/tests/components/home/`:
  - `BootHero` renders the download and guide links with the `/formatkit` prefix in both locales, the prompt and the given log lines, and the pause button.
  - `CatalogTexture` output is `aria-hidden="true"` and contains every id passed.
  - `CatalogStat` total equals the item count, per-category counts match a fixture, labels come from i18n, an unknown category still renders.
  - `HomeSection` renders `num` and `label`.
- The existing i18n parity test covers the new keys.
- A test that `ThemeSelect` override renders no `<select>` and `ThemeProvider` sets the dark theme.
- `npm run check`, `npm test`, `npm run build`, `npm run check:dist` all exit 0; `check:dist` must not need any new allowed host.
- Manual: look at home and one catalog item page at 1440 px and 375 px, and with reduced motion on.

## Found while building

- Starlight renders its own PageTitle h1 on pages without `hero`; the home keeps `hero: {}` and a `Hero` override renders nothing for an empty hero, but must render Starlight's default Hero otherwise (the 404 page is a hero-only splash page).
- Motion-off must not force `opacity: 1` on the texture (only on the animated log lines); the texture rests at 0.07 (0.04 below 50rem for readability on phones).
- Step cards follow the real module order (drivers -> cleanup -> programs, per README), not the order first written in section 6.
- The undo card only claims what `formatkit cleanup undo` covers: cleanup tweaks.
- The `[ WARN ]` caution is a full-width amber line under the hero grid; the hero prompt's tokens are `nowrap` spans so flags never break.
- `check:dist`'s command check reads code spans and fences in the MDX sources, not the built HTML, so the hero prompt (an i18n string) is not checked by it; `boot-hero.test.ts` pins the prompt instead.
- CatalogTexture repeats the ids to ~20000 characters so the drift never shows a blank strip.
