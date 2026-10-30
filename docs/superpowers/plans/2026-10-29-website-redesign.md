# Website Visual Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the FormatKit site the dark "phosphor terminal" look and a new home page, without changing content, data or deployment.

**Architecture:** Starlight stays. A token stylesheet maps the phosphor palette onto `--sl-*` variables; `ThemeProvider`/`ThemeSelect` overrides force dark; four new Astro components in `src/components/home/` build the home page from MDX in each locale; catalog numbers and ids come from the `cleanup` collection.

**Tech Stack:** Astro 7.4 + Starlight 0.42, MDX, TypeScript, vitest 5 with the Astro Container API, `@fontsource-variable/inter` 5.3.0 and `@fontsource-variable/jetbrains-mono` 5.3.0.

**Spec:** `docs/superpowers/specs/2026-10-29-website-redesign-design.md` (looks and home page). Content, i18n, checks and deployment stay governed by `docs/superpowers/specs/2026-10-29-website-design.md`.

## Global Constraints

- All work in `site/`; run commands from `site/`. Node 24.
- Dark only; no theme selector rendered anywhere.
- Colours only through the tokens in spec §4 (`--fk-*`), exact hex values from that table. Text/background pairs ≥ 4.5:1 (≥ 3:1 at 24 px and up).
- Fonts bundled from `@fontsource-variable/*`; `npm run check:dist` must pass with no new allowed host.
- Every internal link carries the `/formatkit` base (`import.meta.env.BASE_URL` or `siteHref`); `formatkit …` commands inside `<code>` must exist in the README table (the hero prompt is `formatkit run --profile cleanup-balanced.json --dry-run`).
- `src/content/i18n/en.json` and `pt-BR.json` keep identical keys; user text in pt-BR and en changes together.
- Motion: CSS only; stopped by `@media (prefers-reduced-motion: reduce)` and by `html[data-motion="off"]`; `localStorage` key `fk-motion`, every access in `try/catch`.
- Components take optional props and fall back to the collection/i18n, like the existing components.
- Verify before each commit: `npm run check` exits 0. Tasks that touch pages also run `npm run build && npm run check:dist` (exit 0).
- Commits: conventional (`feat(site): …`, `style(site): …`), ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never push.

## Review Focus

1. **Phone width (375 px):** the pt-BR `h1` ("Windows limpo. Máquina pronta.") and the giant catalog number must not cause horizontal scroll; grids collapse to one column. Pinned by a CSS rule check in Task 4's manual step and a `overflow-wrap`/`clamp()` requirement in Task 1.
2. **JavaScript off or `localStorage` throwing:** the page still reads fully, all log lines visible at the end of the animation; the pause button is hidden without JS (it is `hidden` in markup and revealed by script). Test in Task 3.
3. **Catalog grows a new category:** `CatalogStat` still counts it, under its raw title-cased name, after the known ones. Test in Task 2.
4. **Empty or tiny id list:** `CatalogTexture` with `ids: []` renders an empty `aria-hidden` box and no error; with a few ids it repeats them to fill. Test in Task 2.
5. **English page links:** every hero and section link on `/en/` points into `/formatkit/en/…`, not the pt-BR page. Test in Task 3 and checked by the links validator in Task 4.

---

### Task 1: Theme foundation (tokens, fonts, dark only, site-wide styles)

**Files:**
- Modify: `site/package.json`, `site/package-lock.json` (fonts)
- Create: `site/src/styles/theme.css`
- Create: `site/src/components/overrides/ThemeProvider.astro`, `site/src/components/overrides/ThemeSelect.astro`
- Modify: `site/astro.config.mjs`
- Test: `site/tests/components/theme-overrides.test.ts`

**Interfaces:**
- Produces: CSS custom properties `--fk-bg`, `--fk-panel`, `--fk-line`, `--fk-rule`, `--fk-text`, `--fk-body`, `--fk-dim`, `--fk-green`, `--fk-on-green`, `--fk-amber`, `--fk-amber-bg`; font stacks `--fk-mono` (`'JetBrains Mono Variable', ui-monospace, Consolas, monospace`) and `--fk-sans` (`'Inter Variable', system-ui, sans-serif`). Utility classes used by later tasks: `.fk-btn` (green pill with glow), `.fk-link-dim` (dim text link with arrow), `.fk-label` (uppercase mono, wide tracking), `.fk-sub` (italic dim subline), `.fk-panel` (panel box with border).

- [ ] **Step 1: Install fonts** — `npm install @fontsource-variable/inter@5.3.0 @fontsource-variable/jetbrains-mono@5.3.0`.

- [ ] **Step 2: Write the failing test** `tests/components/theme-overrides.test.ts`:
  - `ThemeSelect` renders no `<select>` and no `starlight-theme-select` (`expect(html).not.toMatch(/<select|starlight-theme-select/)`).
  - `ThemeProvider` output contains `dataset.theme = 'dark'` (or `data-theme="dark"`) and defines `window.StarlightThemeProvider` with an `updatePickers` function (`expect(html).toContain('StarlightThemeProvider')`, `toContain('updatePickers')`), and does not read `prefers-color-scheme`.

- [ ] **Step 3: Run** `npx vitest run tests/components/theme-overrides.test.ts` — expect FAIL (files missing).

- [ ] **Step 4: Implement the overrides.** `ThemeProvider.astro`: an `is:inline` script that sets `document.documentElement.dataset.theme = 'dark'` and assigns `window.StarlightThemeProvider = { updatePickers() {} }` (Starlight code calls it). `ThemeSelect.astro`: renders nothing.

- [ ] **Step 5: Write `src/styles/theme.css`.** Imports `@fontsource-variable/inter` and `@fontsource-variable/jetbrains-mono`. Defines the tokens on `:root` and maps them onto Starlight for `:root, :root[data-theme='dark']`: `--sl-color-bg`/`--sl-color-bg-nav`/`--sl-color-bg-sidebar` → `--fk-bg`, `--sl-color-white` → `--fk-text`, `--sl-color-gray-1`/`-2` → `--fk-body`, `--sl-color-gray-3` → `--fk-dim`, `--sl-color-gray-5`/`-6` → `--fk-line`, `--sl-color-hairline*` → `--fk-rule`, `--sl-color-accent*` → green shades, `--sl-color-text-accent` → `--fk-green`, `--sl-font` → `--fk-sans`, `--sl-font-mono` → `--fk-mono`. Site-wide rules from spec §5:
  - header translucent `--fk-bg` at 85% with `backdrop-filter: blur(8px)`; `.site-title` mono, `text-transform: lowercase`, `::after` a 0.5em × 1em green block;
  - sidebar links mono; current link green with a 2px left bar;
  - `.sl-markdown-content h2, h3` mono; `.sl-markdown-content h2::before { content: '## '; color: var(--fk-line); }`;
  - `.starlight-aside--caution, --danger` amber on `--fk-amber-bg`, `--note, --tip` green border on `--fk-panel`;
  - footer text dim mono;
  - `h1, h2` use `overflow-wrap: anywhere` and `clamp()` sizes so nothing overflows at 375 px;
  - the utility classes listed under Interfaces.

- [ ] **Step 6: Wire it in `astro.config.mjs`:** `customCss: ['./src/styles/theme.css']`; `components: { ThemeProvider: './src/components/overrides/ThemeProvider.astro', ThemeSelect: './src/components/overrides/ThemeSelect.astro' }`; `expressiveCode: { themes: ['github-dark'], styleOverrides: { /* github-dark supplies green-ish strings and dim comments; panel colours come from the tokens */ codeBackground: 'var(--fk-panel)', borderColor: 'var(--fk-line)', codeFontFamily: 'var(--fk-mono)', uiFontFamily: 'var(--fk-mono)' } }`.

- [ ] **Step 7: Verify** — `npm run check` exit 0; `npm run build && npm run check:dist` exit 0; `grep -L 'theme-select' dist/index.html` lists the file (no selector); fonts are under `dist/_astro/` (`ls dist/_astro/*.woff2`).

- [ ] **Step 8: Commit** — `style(site): phosphor dark theme, bundled fonts, dark-only overrides`.

### Task 2: Data-driven home components (`HomeSection`, `CatalogStat`, `CatalogTexture`)

**Files:**
- Create: `site/src/components/home/HomeSection.astro`, `CatalogStat.astro`, `CatalogTexture.astro`
- Modify: `site/src/content/i18n/en.json`, `pt-BR.json` (key `home.stat.items`)
- Test: `site/tests/components/home/home-data.test.ts`

**Interfaces:**
- Consumes: `CleanupItem` (`src/loaders/cleanup.ts`), `KNOWN_CATEGORIES`, `labelFor` (`src/lib/labels.ts`), `labelsFor`, `localeFromAstro`, `t`, `SiteLocale` (`src/lib/text.ts`); classes from Task 1.
- Produces:
  - `HomeSection.astro` props `{ num: string; label: string; id?: string }`, default slot; root `<section class="fk-home-section" id={id}>`.
  - `CatalogStat.astro` props `{ locale?: SiteLocale; items?: CleanupItem[] }`; renders the total in an element with class `fk-stat-total`, then one `.fk-stat-cat` per category with the label and count.
  - `CatalogTexture.astro` props `{ ids?: string[] }`; root `<div class="fk-texture" aria-hidden="true">`.
  - i18n `home.stat.items`: pt-BR `"ajustes no catálogo"`, en `"tweaks in the catalog"`.

- [ ] **Step 1: Write the failing tests** (fixture of 5 items: 2 `apps`, 1 `privacy`, 1 `developer`, 1 `gaming`):
  - `CatalogStat` en: contains `fk-stat-total` with `5`; category order Apps, Privacy, Developer, then Gaming (known order first, unknown title-cased after; assert with `indexOf`); counts `2`, `1`, `1`, `1`; contains `tweaks in the catalog`. pt-BR shows `Privacidade` and `ajustes no catálogo`.
  - `CatalogTexture` with `ids: ['apps.a', 'privacy.b']`: `aria-hidden="true"`, contains both ids; with `ids: []`: renders `fk-texture` and no id text, no throw.
  - `HomeSection` with `num: '03'`, `label: 'O catálogo'` and slot `<p>x</p>`: contains `03`, `O catálogo`, `<p>x</p>`.

- [ ] **Step 2: Run** `npx vitest run tests/components/home/home-data.test.ts` — expect FAIL.

- [ ] **Step 3: Implement the three components.** Collection fallback exactly like `CatalogIndex.astro` (dynamic `import('astro:content')`). `CatalogTexture` sorts ids and repeats the joined string until about 4000 characters (stop when the list is empty); a slow `translateY` drift keyframe, stopped per Global Constraints. `CatalogStat` big number: mono 900 weight, `font-size: clamp(5rem, 18vw, 10rem)`, transparent fill with `-webkit-text-stroke: 1px var(--fk-green)` and a repeating-linear-gradient scan-line fill clipped to text. `HomeSection`: two-column grid (`7rem 1fr`), one column under 50rem; number in green mono, label with `.fk-label`.

- [ ] **Step 4: Run tests** — PASS; `npm run check` exit 0.

- [ ] **Step 5: Commit** — `feat(site): home section, catalog stat and catalog texture components`.

### Task 3: `BootHero` with log animation and pause control

**Files:**
- Create: `site/src/components/home/BootHero.astro`
- Modify: `site/src/content/i18n/en.json`, `pt-BR.json`
- Test: `site/tests/components/home/boot-hero.test.ts`

**Interfaces:**
- Consumes: `CatalogTexture` (Task 2), `siteHref` (`src/lib/catalogPaths.ts`), `t`, `localeFromAstro`, `SiteLocale`; classes from Task 1.
- Produces: `BootHero.astro` props `{ locale?: SiteLocale; title: string; subtitle: string; lead: string; logLines?: { status: 'ok' | 'run'; text: string }[] }`. Named slot `warn` for the `[ WARN ]` line. Renders: label link to download, `<h1>`, `.fk-sub`, lead `<p>`, primary `.fk-btn` → `siteHref(base, locale, 'download')`, `.fk-link-dim` → `guide/before`, dim links → `catalog` and `https://github.com/jpst-dev/formatkit`; log box with `<code>{t(locale,'home.log.prompt')}</code>` and one line per log entry (`[ OK ]` or `[ .. ]`), the last followed by a cursor span; a `<button class="fk-motion" hidden>`; `CatalogTexture` behind.
- i18n keys (both files):
  - `home.label`: pt-BR `"FormatKit · Windows 11 x64 →"`, en same.
  - `home.cta.download`: `"Baixar"` / `"Download"`; `home.cta.guide`: `"Ler o guia ↓"` / `"Read the guide ↓"`; `home.cta.catalog`: `"Ver o catálogo"` / `"Browse the catalog"`.
  - `home.log.prompt`: `"formatkit run --profile cleanup-balanced.json --dry-run"` (both).
  - `home.log.1`…`home.log.5`: pt-BR `"Hardware detectado"`, `"Drivers que faltam: plano pronto"`, `"Programas do perfil: plano pronto"`, `"privacy.advertising-id"`, `"performance.game-mode"`; en `"Hardware detected"`, `"Missing drivers: plan ready"`, `"Profile programs: plan ready"`, same two ids. Lines 1-4 `ok`, line 5 `run`.
  - `home.motion.pause`: `"Pausar animação"` / `"Pause motion"`; `home.motion.play`: `"Retomar animação"` / `"Play motion"`.

- [ ] **Step 1: Write the failing tests:**
  - en: links contain `href="/formatkit/en/download/"`, `/formatkit/en/guide/before/`, `/formatkit/en/catalog/`; contains `formatkit run --profile cleanup-balanced.json --dry-run` inside `<code>`; contains `Hardware detected` and four `[ OK ]` and one `[ .. ]` (count with `match(/\[ OK \]/g)`).
  - pt-BR: links `/formatkit/download/` and `/formatkit/guide/before/`; `Hardware detectado`.
  - custom `logLines` prop replaces the defaults.
  - the motion button exists with the `hidden` attribute and text `Pause motion`; the inline script reads `fk-motion` inside `try`.
  - the `warn` slot content is rendered.

- [ ] **Step 2: Run** `npx vitest run tests/components/home/boot-hero.test.ts` — expect FAIL.

- [ ] **Step 3: Implement.** Two-column grid (`1.1fr 1fr`), one column under 50rem. Log lines fade in with `animation-delay: calc(var(--i) * 450ms)`, `animation-fill-mode: both`; cursor blink 1s steps. A rule `@media (prefers-reduced-motion: reduce), html[data-motion='off'] *` cannot be combined — write two blocks that set `animation: none !important; opacity: 1` on `.fk-hero *` and `.fk-texture`. Inline script (`is:inline`): in `try`, read `localStorage.getItem('fk-motion')`, set `document.documentElement.dataset.motion` when `'off'`; un-hide the button; on click toggle the attribute, swap the label between `data-pause`/`data-play` attribute texts, write `localStorage` in `try`.

- [ ] **Step 4: Run tests** — PASS; `npm run check` exit 0.

- [ ] **Step 5: Commit** — `feat(site): boot hero with dry-run log and motion control`.

### Task 4: New home pages (pt-BR and en)

**Files:**
- Modify: `site/src/content/docs/index.mdx`, `site/src/content/docs/en/index.mdx`
- Modify (only if the dev server refuses the raw import): `site/astro.config.mjs` (`vite.server.fs.allow: ['..']`)

**Interfaces:**
- Consumes: `BootHero`, `HomeSection`, `CatalogStat` (Tasks 2-3); Starlight `Code` component; `../../../../profiles/cleanup-balanced.json?raw` (path from `src/content/docs/`; one more `../` from `en/`).

- [ ] **Step 1: Rewrite `index.mdx`** (keep `title`, `description`, `template: splash`; remove `hero`). Body, copy exactly from spec §6:
  - `<BootHero title="Windows limpo. Máquina pronta." subtitle="Com o mínimo de cliques." lead="<today's tagline>">` with the `warn` slot: `[ WARN ] Esta ferramenta altera o sistema: …` — today's caution text unchanged, including `--dry-run` in a code span.
  - `HomeSection num="01" label="Como funciona"`: heading "Formatou. Rodou um perfil." + `.fk-sub` "Voltou a trabalhar." + four `.fk-panel` step cards (Detectar / Drivers / Programas / Limpeza, one short line each).
  - `02` "O perfil": "Um arquivo." / "Não uma imagem." + one paragraph (today's "Instalação limpa, não clonada" card text) + `<Code code={profile} lang="json" title="profiles/cleanup-balanced.json" />`.
  - `03` "O catálogo": `<CatalogStat />` + sentence + link `/formatkit/catalog/`.
  - `04` "Segurança": "Tudo tem volta." / "E tudo fica no log." + two cards (`--dry-run`; undo journal) linking `/formatkit/safety/`.
  - `05` "Dois jeitos": two cards `FormatKitUI.exe` / `formatkit.exe` (today's third card text) linking `/formatkit/download/`.
  - Closing: "Pronto para formatar?" + "Windows 11 x64 · sem instalar o .NET · SHA-256 publicado em cada versão" + `.fk-btn` to download.
  - Section headings are `<h2>` with no `## ` prefix (add `class="fk-home-h"`; theme.css excludes it from the prefix rule).

- [ ] **Step 2: Mirror in `en/index.mdx`** with English copy ("Clean Windows. Ready machine." / "With as few clicks as possible."; "Formatted. Ran a profile." / "Back to work."; "One file." / "Not an image."; "The catalog"; "Everything can be undone." / "And everything is logged."; "Two ways"; "Ready to format?"), links under `/formatkit/en/`.

- [ ] **Step 3: Verify** — `npm run check` exit 0; `npm run build` (links validator passes) and `npm run check:dist` exit 0; `grep -c 'fk-stat-total' dist/index.html dist/en/index.html` prints 1 each; `grep -c 'cleanup-balanced.json --dry-run' dist/en/index.html` ≥ 1.

- [ ] **Step 4: Manual look** — `npm run dev`, open `/formatkit/` and `/formatkit/en/` at 1440 px and 375 px: no horizontal scroll (`document.documentElement.scrollWidth <= innerWidth` in the console), hero collapses to one column, pause button stops the animation and survives a reload; with reduced motion emulated, all log lines are visible at once.

- [ ] **Step 5: Commit** — `feat(site): phosphor home page in both languages`.

### Task 5: Catalog badges and item page styling

**Files:**
- Modify: `site/src/components/CatalogItem.astro`, `site/src/components/CatalogIndex.astro`
- Test: `site/tests/components/catalog-item.test.ts`

**Interfaces:**
- Consumes: tokens from Task 1.
- Produces: badge markup `<span class="fk-badge" data-tone="green|amber|plain">`; item id line `<p class="fk-item-id"><code>{item.id}</code></p>` as the first child of `.fk-item`, before the badges.

- [ ] **Step 1: Add failing tests** to `catalog-item.test.ts`:
  - item page contains `<code>apps.x</code>` inside `fk-item-id`.
  - the level badge for `level: 'safe'` has `data-tone="green"`, for `aggressive` `data-tone="amber"`, for `balanced` `data-tone="plain"`; the contested badge has `data-tone="amber"`; category/scope badges `plain`. Same tones on `CatalogIndex` flags.

- [ ] **Step 2: Run** `npx vitest run tests/components/catalog-item.test.ts` — expect FAIL.

- [ ] **Step 3: Implement.** Build badges as `{ text, tone }` objects; tone rule: level `safe` → green, `aggressive` → amber, contested → amber, everything else plain. Styles: square (3px radius) mono tags, 1px `--fk-line` border; green tone border and text `--fk-green` with a `●` before; amber tone `--fk-amber`. `.fk-item-id code` green mono, no background.

- [ ] **Step 4: Verify** — tests PASS, `npm run check` exit 0, `npm run build && npm run check:dist` exit 0.

- [ ] **Step 5: Commit** — `style(site): terminal-style catalog badges and item id`.

### Task 6: Whole-site visual pass and spec record

**Files:**
- Modify: `site/src/styles/theme.css` (fixes only), `docs/superpowers/specs/2026-10-29-website-redesign-design.md` ("Found while building")

- [ ] **Step 1: Look at every page type** at 1440 px and 375 px in the dev server: home (both), download, one guide page, catalog index, one catalog item, safety, FAQ, changelog, a 404. Check: no light-theme remnants (white backgrounds, unreadable text), asides in amber/green, code blocks on `--fk-panel`, search dialog readable, no horizontal scroll.

- [ ] **Step 2: Fix what is found in `theme.css`** (no content changes).

- [ ] **Step 3: Record** what was found and how it was fixed under "Found while building" in the redesign spec (or "Nothing found." if clean).

- [ ] **Step 4: Verify** — `npm run check`, `npm run build`, `npm run check:dist` all exit 0.

- [ ] **Step 5: Commit** — `style(site): redesign visual pass` (or `docs: record redesign findings` if only the spec changed).
