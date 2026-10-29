import { readFile } from 'node:fs/promises';
import path from 'node:path';
import type { Loader } from 'astro/loaders';
import { warn } from '../lib/warnings';

export type ReleaseAsset = { name: string; url: string; size: number; sha256: string | null };
export type Release = {
  id: string;
  tag: string;
  name: string;
  date: string;
  url: string;
  prerelease: boolean;
  body: string;
  assets: ReleaseAsset[];
};
export type ReleasesResult = { releases: Release[]; latest: Release | null };

type ApiAsset = { name: string; browser_download_url: string; size: number; digest?: string | null };
type ApiRelease = {
  tag_name: string;
  name: string | null;
  published_at: string | null;
  created_at?: string;
  html_url: string;
  draft: boolean;
  prerelease: boolean;
  body: string | null;
  assets?: ApiAsset[];
};

const API_URL = 'https://api.github.com/repos/jpst-dev/formatkit/releases';
const KEPT_ASSETS = new Set(['FormatKitUI.exe', 'formatkit.exe', 'SHA256SUMS.txt']);
const SUMS = 'SHA256SUMS.txt';

type Opts = { fetch: typeof fetch; env: Record<string, string | undefined> };

function parseSums(text: string): Map<string, string> {
  const map = new Map<string, string>();
  for (const line of text.split(/\r?\n/)) {
    const m = /^([0-9a-fA-F]{64})\s+\*?(.+?)\s*$/.exec(line);
    if (m) map.set(m[2], m[1].toLowerCase());
  }
  return map;
}

function digestOf(a: ApiAsset): string | null {
  const m = /^sha256:([0-9a-fA-F]{64})$/i.exec(a.digest ?? '');
  return m ? m[1].toLowerCase() : null;
}

function when(r: ApiRelease): string {
  return r.published_at ?? r.created_at ?? '';
}

async function readApi(opts: Opts): Promise<{ api: ApiRelease[]; sums: (a: ApiAsset) => Promise<string> }> {
  const { env } = opts;
  if (env.FK_RELEASES_FILE) {
    const file = env.FK_RELEASES_FILE;
    const api = JSON.parse(await readFile(file, 'utf8')) as ApiRelease[];
    return { api, sums: (a) => readFile(path.join(path.dirname(file), a.name), 'utf8') };
  }
  const headers: Record<string, string> = {
    Accept: 'application/vnd.github+json',
    'X-GitHub-Api-Version': '2022-11-28',
  };
  if (env.GITHUB_TOKEN) headers.Authorization = `Bearer ${env.GITHUB_TOKEN}`;
  const res = await opts.fetch(`${API_URL}?per_page=100`, { headers });
  if (!res.ok) throw new Error(`GitHub API answered HTTP ${res.status}`);
  const api = (await res.json()) as ApiRelease[];
  if (!Array.isArray(api)) throw new Error('GitHub API answered an unexpected payload');
  return {
    api,
    sums: async (a) => {
      const r = await opts.fetch(a.browser_download_url);
      if (!r.ok) throw new Error(`${SUMS} answered HTTP ${r.status}`);
      return r.text();
    },
  };
}

export async function loadReleases(opts: Opts): Promise<ReleasesResult> {
  const required = opts.env.FK_REQUIRE_RELEASE === '1';
  let releases: Release[] = [];
  let latest: Release | null = null;
  let reason: string | null = null;

  try {
    const { api, sums } = await readApi(opts);
    const kept = api.filter((r) => !r.draft).sort((a, b) => Date.parse(when(b)) - Date.parse(when(a)));

    for (const r of kept) {
      const assets: ReleaseAsset[] = [];
      let table: Map<string, string> | null = null;
      for (const a of (r.assets ?? []).filter((x) => KEPT_ASSETS.has(x.name))) {
        let sha256 = digestOf(a);
        if (!sha256 && a.name !== SUMS) {
          const sumsAsset = (r.assets ?? []).find((x) => x.name === SUMS);
          if (sumsAsset) {
            try {
              table ??= parseSums(await sums(sumsAsset));
              sha256 = table.get(a.name) ?? null;
            } catch (e) {
              table = new Map();
              warn('releases.unavailable', `${SUMS} of ${r.tag_name}: ${(e as Error).message}`);
            }
          }
        }
        assets.push({ name: a.name, url: a.browser_download_url, size: a.size, sha256 });
      }
      releases.push({
        id: r.tag_name,
        tag: r.tag_name,
        name: r.name || r.tag_name,
        date: when(r),
        url: r.html_url,
        prerelease: r.prerelease,
        body: r.body ?? '',
        assets,
      });
    }
    latest = releases.find((r) => !r.prerelease) ?? null;
    if (!latest) reason = releases.length ? 'only prereleases published' : 'no release published';
  } catch (e) {
    reason = (e as Error).message;
    releases = [];
    latest = null;
  }

  if (reason) {
    if (required) throw new Error(`Latest release not readable: ${reason}`);
    warn('releases.unavailable', reason);
  }
  return { releases, latest };
}

export function releasesLoader(): Loader {
  return {
    name: 'fk-releases',
    async load({ store, parseData }) {
      const { releases } = await loadReleases({ fetch: globalThis.fetch, env: process.env });
      store.clear();
      for (const r of releases) {
        store.set({ id: r.id, data: await parseData({ id: r.id, data: r }) });
      }
    },
  };
}

export async function getLatestRelease(): Promise<Release | null> {
  const { getCollection } = await import('astro:content');
  const all = (await getCollection('releases')).map((e) => e.data as Release);
  return all.filter((r) => !r.prerelease).sort((a, b) => Date.parse(b.date) - Date.parse(a.date))[0] ?? null;
}
