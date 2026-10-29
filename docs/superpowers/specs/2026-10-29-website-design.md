# FormatKit — Website Design

- **Date:** 2026-10-29
- **Status:** Approved in away mode (decisions D1-D16 taken on the author's
  recommendation; the user reviews them on return)
- **Scope:** A public static website for FormatKit in pt-BR and English,
  built from this repository and published to GitHub Pages
- **Builds on:** `2026-10-27-cleanup-design.md` (cleanup catalog, levels,
  `contested`, `laptopSensitive`, undo journal, `cleanup check`),
  `2026-10-28-programs-design.md` (program catalog),
  `2026-10-29-preparation-checklist-design.md` (`prepare check`),
  `2026-10-29-wpf-ui-design.md` (`FormatKitUI.exe`) and, when merged,
  `2026-10-29-installation-media-design.md` (`media build`, `media usb`)

## 1. Purpose

Give FormatKit a public home that a non-developer can follow from "should I
format?" to a working PC, and that explains every cleanup item honestly:
what it does, what may break, when to use it and how strong the evidence is.

The site never drifts from the app: catalog, known-issue and program pages
are generated at build time from the same JSON files the app embeds, and the
download page reads the published GitHub Release.

### Success criteria

- A visitor can download the latest release, check its SHA-256 and get past
  SmartScreen knowing why it appears.
- A visitor can follow the guide end to end (backup, media, install, profile,
  after-install) without reading the README.
- Every cleanup item in `cleanup.json` has a page in both languages, and a
  change to the JSON changes the site on the next build with no hand edits.
- A new category, level, action type or field added by sub-project 1 does not
  break the build; it shows up with a generic rendering and a build warning.
- Measurements are presented as one VM's before/after snapshots with method
  and limits, never as FPS or speed gains.
- The built site makes no request to any host other than itself.

## 2. Scope

**In:**

- An Astro + Starlight project in `site/`.
- Pages: Home, Download, Guide (five pages), Catalog (index plus one page per
  item), Measurements, Safety & undo, Recommendations, FAQ, Changelog.
- Build-time loaders for `cleanup.json`, `known-issues.json`,
  `programs.json`, the measurement dataset and the GitHub Releases API.
- A `pages.yml` workflow that builds and deploys, called from `release.yml`
  and runnable by hand; a `site.yml` workflow that builds and tests the site
  on pushes and pull requests that touch it or its data.
- Two `release.yml` changes: a `SHA256SUMS.txt` asset and generated release
  notes.
- Screenshots of `FormatKitUI.exe` taken in the test VM.

**Out of scope:** a custom domain (section 10 says how to add one later);
analytics, comments, newsletters or any third-party embed; a blog; editing or
building profiles in the browser; translating release notes; code signing.

## 3. Decisions

| # | Decision | Reason |
|---|---|---|
| D1 | Astro + Starlight in `site/`, with build-time loaders that read the repository's JSON (approach 1). | Starlight brings i18n, local search, navigation and accessibility; the JSON stays the single source. A .NET generator (approach 2) would reuse `CatalogValidator` but add .NET and generated files to the site build, and the unit tests already validate the JSON in CI. Plain Astro (approach 3) would rebuild what Starlight gives. |
| D2 | pt-BR is the root locale (`/formatkit/`); English is at `/formatkit/en/`. | The guide is written for Brazilian users (ABNT keyboards, US-International layout). |
| D3 | The same English slugs in both locales (`/formatkit/guide/before/`, `/formatkit/en/guide/before/`). | Starlight's language switcher maps pages by slug; localized slugs would need a mapping table. |
| D4 | Every docs page exists in both locales; the check fails when one is missing. | Starlight's fallback silently shows the other language; a missing translation should be visible in CI, not to visitors. |
| D5 | The loaders validate with zod in tolerant mode: categories, levels and action types are strings, unknown fields pass through. Only a missing or duplicate `id` or a missing `title`/`what`/`breaks` in either language fails the build. | Sub-project 1 is adding categories and maybe fields; the site must keep building and show them, while still refusing items it cannot describe. |
| D6 | Item pages are a custom route (`src/pages/[...locale]/catalog/[id].astro`) wrapped in `<StarlightPage>`; all other pages are MDX in the docs collection that embed data components. | Starlight's docs collection is file-based; one route generates every item in both locales. |
| D7 | Charts are SVG rendered at build time by an Astro component, each with the same numbers in a table. | No client JavaScript or chart library, works without JS, readable by screen readers, and the table keeps the numbers checkable. |
| D8 | The measurement dataset is copied into `site/src/data/measurements/<run-id>/` with a `run.json` manifest that records where it came from. | The public site cannot depend on `research_notes`, which is outside the repository; the manifest keeps the provenance. |
| D9 | Deploy is a reusable workflow (`pages.yml`, `workflow_call` + `workflow_dispatch`) called as the last job of `release.yml`. | A release created with `GITHUB_TOKEN` does not trigger `release: published` in other workflows, so a separate trigger would never fire. |
| D10 | The release adds `SHA256SUMS.txt`; the Download page shows the asset `digest` from the Releases API and the `Get-FileHash` command to check it. | Users can verify without trusting the page alone; the sums file works even where the API field is missing. |
| D11 | A release-triggered deploy fails if the latest release cannot be read; a manual deploy shows "no release published yet" instead. | A published download page without a hash is worse than no deploy; the first deploy may happen before the first release. |
| D12 | Release notes come from `generate_release_notes: true` and are shown in English on both locales, rendered with `micromark` (raw HTML off). | Notes are not translated (out of scope); `micromark` is CommonMark-safe by default. |
| D13 | Home's button goes to the Download page, not straight to the `.exe`. | The visitor sees the SmartScreen note and the hash before running anything. |
| D14 | The guide describes only what a published release contains. The media page uses `formatkit media usb` once PR #1 (installation media) is in a release; until then it points to Microsoft's Media Creation Tool. | The site must not document commands a visitor cannot download. |
| D15 | Screenshots are taken in the test VM (`tools/vm/vmshot.ps1`) from the pt-BR UI and used in both locales with localized alt text. | The VM is pt-BR; a second English capture set doubles the upkeep for little gain. |
| D16 | No analytics, no external fonts or scripts; a post-build check fails if any built file references another host for a script, style, font, image or frame. Search is Starlight's local Pagefind. | The user asked for no third-party tracking; a check keeps it true over time. |

## 4. Architecture

```
repository
├─ src/FormatKit.Cleanup/Catalog/Data/cleanup.json      ─┐
├─ src/FormatKit.Cleanup/Catalog/Data/known-issues.json  ├─ read at build time
├─ src/FormatKit.Programs/Catalog/Data/programs.json    ─┘
├─ .github/workflows/release.yml  → tests, publish, release, then pages.yml
├─ .github/workflows/pages.yml    → npm ci, check, build, deploy to Pages
├─ .github/workflows/site.yml     → npm ci, check, build (push / PR, paths)
└─ site/
   ├─ astro.config.mjs        site, base '/formatkit', Starlight, locales
   ├─ src/content.config.ts   docs + i18n collections, data collections
   ├─ src/loaders/            cleanup, knownIssues, programs, releases, measurements
   ├─ src/lib/                pure helpers (text pick, labels, chart scales)
   ├─ src/components/         CatalogIndex, CatalogItem, KnownIssues,
   │                          ProgramList, DownloadCard, ReleaseNotes,
   │                          MeasurementRun, BarChart
   ├─ src/pages/[...locale]/catalog/[id].astro
   ├─ src/content/docs/       pt-BR MDX pages
   ├─ src/content/docs/en/    English MDX pages
   ├─ src/content/i18n/       pt-BR.json, en.json (UI strings of components)
   ├─ src/data/measurements/  datasets (section 8)
   ├─ src/assets/screenshots/ VM screenshots
   ├─ scripts/check-dist.mjs  post-build checks (section 11)
   └─ tests/                  vitest: loaders, helpers, fixtures
```

Each loader is one file with one job: read a source, validate it, return
entries. Components receive entries and a locale and render; they do not read
files or call the network. `src/lib` holds the logic that tests cover without
Astro (picking a locale's text, ordering levels, labels for unknown values,
chart scales).

Node 24 LTS is pinned in `site/.nvmrc` and `engines`; `package-lock.json` is
committed. Versions at writing: Astro 7.4, Starlight 0.42.

## 5. Pages

| Slug | Source | Content |
|---|---|---|
| `/` | `index.mdx` (splash) | What FormatKit does in three lines, a UI screenshot, "Baixar"/"Download" to the Download page, the warning that it changes the system, links to the guide and the catalog. |
| `download` | MDX + `DownloadCard` | Latest version and date, both assets (`FormatKitUI.exe`, `formatkit.exe`) with size and SHA-256, which one to pick, requirements (Windows 11 x64, no .NET needed), the SmartScreen note (unsigned, "More info" → "Run anyway"), how to check the hash, build from source link. |
| `guide/before` | MDX | Back up, run `formatkit prepare check` (or the Checklist page of the UI), `formatkit programs export`, `formatkit drivers backup-network`, write down licenses the checklist cannot see. |
| `guide/media` | MDX | Make the USB stick (D14). |
| `guide/install` | MDX | Boot from the stick, install, first logon. |
| `guide/profile` | MDX | Write or pick a profile, preview with `--dry-run` or the plan view, run, what reboots and resume look like. |
| `guide/after` | MDX | `formatkit drivers scan` and the leftover-device report, default apps (set by hand in Settings; Windows does not allow tools to set them), keyboard: a physical ABNT keyboard uses "Português (Brasil ABNT)", a US keyboard typing Portuguese uses "Estados Unidos-Internacional", with the Settings path; `formatkit cleanup check`. |
| `catalog` | MDX + `CatalogIndex` | Items grouped by category, ordered by level, with level, `contested`, `laptopSensitive` and reboot badges; a short explanation of levels and flags. |
| `catalog/<id>` | custom route + `CatalogItem` | Section 6. |
| `measurements` | MDX + `MeasurementRun` | Section 8. |
| `safety` | MDX + `KnownIssues` | What FormatKit never does (from the cleanup and programs specs and the README), the undo journal and `cleanup undo`, `cleanup check` and the list of issues it detects (generated from `known-issues.json`), the VM-first warning. |
| `recommendations` | MDX + `ProgramList` | Good practices (a password manager, backups, updates, keep `safe` unless you know why), and the program catalog grouped by category from `programs.json`. Program entries state facts from the JSON only, no rankings. |
| `faq` | MDX | SmartScreen, antivirus false positives, "does it make games faster?" (no claim; link to Measurements), laptops and `laptopSensitive`, undo, offline use, why not a cloned image. |
| `changelog` | MDX + `ReleaseNotes` | Every release, newest first, with date, link and notes (D12). |

The sidebar lists: Download, Guide (group of five), Catalog, Measurements,
Safety & undo, Recommendations, FAQ, Changelog. Item pages are not in the
sidebar; the catalog index links them, and each item page links back.

Text drawn from the README is rewritten for a non-developer; commands keep
their exact spelling from the README command table (section 11 checks it).

## 6. Catalog generation

### Input contract

The cleanup loader reads `cleanup.json` (schema v1 at writing) and accepts:

- `id` (string, unique, required), `category`, `level`, `scope` (strings),
  `contested`, `requiresReboot`, `laptopSensitive` (booleans, default false).
- `text.en` and `text["pt-BR"]`, each with `title`, `what`, `breaks`
  (required) and `gain` (optional). Extra text fields pass through and are
  ignored.
- `actions`: an array of objects with a string `type`.
- `evidence` (optional, not in v1): a string level, or an object with a
  string `level` and optional `sources` (`title`, `url`). Any other shape is
  a warning and is shown as "not stated".
- Any other field passes through and is ignored.

`schemaVersion` greater than 1 is a warning, not an error.

### Rendering

`CatalogItem` shows, in the page's language:

1. Title, then badges: level, category, `contested`, `laptopSensitive`,
   "needs reboot", scope (this user / whole machine).
2. **O que faz / What it does**: `what`.
3. **O que pode quebrar / What may break**: `breaks`.
4. **Quando usar / When to use**: a sentence built from the level and flags
   (`safe`: fine for everyone; `balanced`: when you do not use the feature;
   `aggressive`: only if you understand the trade-off; `contested`: experts
   disagree; `laptopSensitive`: may cost battery or sleep on laptops), then
   `gain` when present.
5. **Evidência / Evidence**: the level and sources, or "not stated yet".
6. **Como desfazer / How to undo**: `formatkit cleanup undo <id>` and the
   undo journal link.
7. **Detalhes técnicos / Technical details** (collapsed): one row per action.
   `appx` shows the package and Store id, `registry` the path, value and data,
   `named` its name, `powerScheme` the scheme. An unknown type shows its
   `type` and its fields as a key/value list.
8. "Seen in the measurements" when a measurement group lists this id
   (section 8).

Labels for known categories and levels live in `src/content/i18n/*.json`. An
unknown category or level is shown with its raw value turned into title case
and listed in a build warning so someone adds a label. Unknown levels sort
after the known ones; unknown categories after the known ones, in order of
first appearance.

### Errors

A hard error stops the build with the item id and the missing field. A
warning is printed once per build and collected in `dist/build-warnings.json`
(not linked from any page) so CI logs show it.

## 7. Download and changelog data

The releases loader calls `GET /repos/jpst-dev/formatkit/releases` with
`GITHUB_TOKEN` when set (CI), else unauthenticated (local builds). It keeps
non-draft releases, newest first; prereleases are shown in the changelog and
never as "latest". For the latest release it keeps tag, date, URL, body and
the assets named `FormatKitUI.exe`, `formatkit.exe` and `SHA256SUMS.txt`
with size and `digest`. When an asset has no `digest`, the loader downloads
`SHA256SUMS.txt` and takes the hash from it.

Failure (network error, rate limit, no release):

- `FK_REQUIRE_RELEASE=1` (set by the release-triggered deploy): build fails.
- Otherwise: the Download page shows "Nenhuma versão publicada ainda / No
  release yet" with a link to the Releases page, the changelog is empty, and
  a warning is recorded.

Local builds and tests can set `FK_RELEASES_FILE=<json>` to read a saved API
response instead of calling the network; the tests use fixtures this way.

## 8. Measurements

### Dataset

`site/src/data/measurements/2026-10-29-vm-clean/`:

- `measure-baseline.json`, `measure-clean-nologon.json`, `measure-G1.json`,
  `measure-G2.json`: copied unchanged from
  `research_notes/Serviços e apps do Windows 11/vm-clean/`.
- `applied-G1.txt`, `applied-G2.txt`: what each group changed.
- `run.json`:
  - `id`, `date`, `machine` (kind "Hyper-V VM", Windows edition and build,
    language, vCPU and memory when known), `source` (the research report
    title, not a local path).
  - `snapshots`: `file`, `label`, condition text in both languages (for
    example "signed in, 25 min after boot").
  - `groups`: `id`, title and summary in both languages, `appliedFile`,
    `baseline` (the snapshot it is compared with), `snapshot`, and
    `catalogIds` (FormatKit items that match the group; empty until
    sub-project 1 maps them).
  - `method` and `limits`: Markdown text in both languages.

The loader validates `run.json` and every snapshot (numbers present and
non-negative) and fails the build on a bad dataset: measurements are only
published when complete.

### Page

For each group: its title and what was applied (the `applied` list in a
collapsed block), then one chart per metric comparing the baseline snapshot
with the group snapshot: running processes, running services, memory in use
(MB), average CPU %, average disk and network throughput. Each chart has the
same numbers in a table with the difference and the conditions of both
snapshots (uptime, signed in or not). The top processes of each snapshot are
a table, not a chart.

The page opens with **Method** and **Limits** before any chart. Limits must
state at least: one VM, one run per state (no repetitions, no error bars);
snapshots at different uptimes and sign-in states; a VM has no GPU, game or
vendor software, so the numbers say nothing about FPS or real hardware; less
memory in use is not by itself faster. The page never says "faster", "FPS"
or a percentage gain in its own words; numbers are shown as measured.

## 9. Ordering

| Phase | Ships | Depends on |
|---|---|---|
| 1 | Scaffold, i18n, Home, Download, Guide, Safety & undo, Recommendations, FAQ, Changelog, workflows, `release.yml` changes, screenshots, checks. | Nothing; the media page follows D14. |
| 2 | Catalog index and item pages, built against schema v1 with tolerant loading. | Can start now; sub-project 1's new categories appear on its merge, then get labels. |
| 3 | Measurements page and dataset. | The final research report (method and limits text) and sub-project 1 for `catalogIds`. |

Each phase is a pull request and can be deployed on its own.

## 10. Deployment

- `pages.yml`: on `workflow_call` (input `require-release`, default true) and
  `workflow_dispatch` (same input, default false). Jobs: build (checkout,
  setup-node from `site/.nvmrc`, `npm ci`, `npm run check`, `npm run build`
  with `GITHUB_TOKEN` and `FK_REQUIRE_RELEASE`), then
  `actions/upload-pages-artifact` and `actions/deploy-pages`. Permissions:
  `contents: read`, `pages: write`, `id-token: write`; concurrency group
  `pages`, no cancel in progress.
- `release.yml`: after the upload, a `site` job (`needs: release`) calls
  `pages.yml`. The release step also writes `SHA256SUMS.txt` (PowerShell
  `Get-FileHash`, `<hash>  <name>` lines, lower case) and sets
  `generate_release_notes: true`.
- `site.yml`: on push and pull request with `paths` `site/**`,
  `src/FormatKit.Cleanup/Catalog/Data/**`,
  `src/FormatKit.Programs/Catalog/Data/**`, `README.md`,
  `.github/workflows/site.yml`, `.github/workflows/pages.yml`; runs check and build without deploying.
- One-time manual step for the user: repository Settings → Pages → Source
  "GitHub Actions". Also Settings → Environments → github-pages → Deployment
  branches and tags → add a tag rule `v*` (so tagged releases may deploy). The
  plan stops for it.
- Custom domain later: set `site` and `base` in `astro.config.mjs`, add
  `site/public/CNAME`, configure DNS. The MDX pages and the home hero actions hard-code `/formatkit`, so those links must be updated too; the links validator flags any that are missed.

## 11. Testing and checks

`npm run check` runs:

- `astro check` (types in components and loaders).
- vitest:
  - cleanup loader with the real `cleanup.json`: every item loads, ids are
    unique, both languages present.
  - cleanup loader with fixtures: an unknown category, level, action type and
    extra field load with warnings; `evidence` as string and as object; a
    missing `breaks` and a duplicate id fail with the id in the message.
  - known-issues and programs loaders with the real files.
  - releases loader with fixtures: latest with digests, latest without
    digests plus a sums file, only prereleases, empty list with and without
    `FK_REQUIRE_RELEASE`.
  - measurements loader with the real dataset and with a broken copy.
  - `src/lib`: level order, labels for unknown values, chart scale (zero
    baseline, max rounding).
- `scripts/check-dist.mjs` after `npm run build`:
  - every docs page has both locales;
  - no built file references another host in `script`, `link`, `img`,
    `iframe` or CSS `url()` (D16); plain `<a href>` links out are allowed;
  - every `formatkit <command>` in the content matches a command in the
    README command table.
- During `npm run build`, the `starlight-links-validator` plugin fails the
  build on an internal link that does not resolve (build-time only; it adds
  nothing to the published pages).

Manual check before each phase's merge: browse the build locally in both
languages, light and dark, at phone width.

## 12. Hand-over to sub-project 1

The site expects, in `cleanup.json`: string ids, the text fields of
section 6, and optionally `evidence` as described there. A new category or
level needs a label in `site/src/content/i18n/*.json`; the build warns until
it has one. The measurement groups' `catalogIds` are filled once the new
items have ids.

## Found while building

- Developer items are all `level: safe`, but they are opt-in: a level never
  selects them and they must be listed in `include`. They get a `when.developer`
  sentence instead of the level sentence, and the catalog page says so.
- `README.md` belongs in `site.yml`'s `paths` because `check:dist` reads it
  (it compares the commands on the site against the README table).
- The GitHub `github-pages` environment needs a `v*` tag rule, or release
  deploys from a tag are rejected.
