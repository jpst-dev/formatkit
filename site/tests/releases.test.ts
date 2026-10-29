import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { beforeEach, expect, test } from 'vitest';
import { loadReleases } from '../src/loaders/releases';
import { readWarnings, resetWarnings } from '../src/lib/warnings';

const fx = (n: string) => fileURLToPath(new URL(`./fixtures/releases/${n}`, import.meta.url));
const noFetch = (async () => {
  throw new Error('network used');
}) as unknown as typeof fetch;
const fixtureEnv = (n: string, extra: Record<string, string> = {}) => ({
  FK_RELEASES_FILE: fx(n),
  ...extra,
});
const H1 = 'a'.repeat(63) + '1';
const H2 = 'b'.repeat(63) + '2';
const H3 = 'c'.repeat(63) + '3';
const empty = { releases: [], latest: null };

beforeEach(() => resetWarnings());

test('with-digests: latest, order, drafts dropped, asset filter, digest normalised', async () => {
  const r = await loadReleases({ fetch: noFetch, env: fixtureEnv('with-digests.json') });
  expect(r.latest?.tag).toBe('v1.1.0');
  expect(r.releases.map((x) => x.tag)).toEqual(['v1.2.0-rc1', 'v1.1.0', 'v1.0.0']);
  expect(r.releases.map((x) => x.id)).toEqual(r.releases.map((x) => x.tag));
  const a = r.latest!.assets;
  expect(a.map((x) => x.name).sort()).toEqual(['FormatKitUI.exe', 'SHA256SUMS.txt', 'formatkit.exe']);
  const exe = a.find((x) => x.name === 'formatkit.exe')!;
  expect(exe.sha256).toBe(H1);
  expect(exe.size).toBe(1000);
  expect(exe.url).toBe('https://example.test/formatkit.exe');
  expect(a.find((x) => x.name === 'FormatKitUI.exe')!.sha256).toBe(H2);
  expect(readWarnings()).toEqual([]);
});

test('no-digests: hashes come from SHA256SUMS.txt next to the JSON; null body is empty', async () => {
  const r = await loadReleases({ fetch: noFetch, env: fixtureEnv('no-digests.json') });
  const a = r.latest!.assets;
  expect(a.find((x) => x.name === 'formatkit.exe')!.sha256).toBe(H3);
  expect(a.find((x) => x.name === 'FormatKitUI.exe')!.sha256).toBe(H2);
  expect(r.latest!.body).toBe('');
});

test('no-digests via network: sums downloaded through the injected fetch', async () => {
  const sums = readFileSync(fx('SHA256SUMS.txt'), 'utf8');
  const json = readFileSync(fx('no-digests.json'), 'utf8');
  const urls: string[] = [];
  const f = (async (url: string) => {
    urls.push(String(url));
    return new Response(String(url).endsWith('SHA256SUMS.txt') ? sums : json);
  }) as unknown as typeof fetch;
  const r = await loadReleases({ fetch: f, env: {} });
  expect(r.latest!.assets.find((x) => x.name === 'formatkit.exe')!.sha256).toBe(H3);
  expect(urls).toContain('https://example.test/SHA256SUMS.txt');
});

test('only-prereleases: no latest, warning', async () => {
  const r = await loadReleases({ fetch: noFetch, env: fixtureEnv('only-prereleases.json') });
  expect(r.latest).toBeNull();
  expect(r.releases).toHaveLength(2);
  expect(readWarnings().map((w) => w.code)).toEqual(['releases.unavailable']);
});

test('only-prereleases with FK_REQUIRE_RELEASE rejects', async () => {
  await expect(
    loadReleases({
      fetch: noFetch,
      env: fixtureEnv('only-prereleases.json', { FK_REQUIRE_RELEASE: '1' }),
    }),
  ).rejects.toThrow(/Latest release not readable/);
});

test('empty: empty result with warning, or rejects when required', async () => {
  expect(await loadReleases({ fetch: noFetch, env: fixtureEnv('empty.json') })).toEqual(empty);
  expect(readWarnings().map((w) => w.code)).toEqual(['releases.unavailable']);
  await expect(
    loadReleases({ fetch: noFetch, env: fixtureEnv('empty.json', { FK_REQUIRE_RELEASE: '1' }) }),
  ).rejects.toThrow(/Latest release not readable/);
});

test('HTTP 403 resolves to empty with a warning', async () => {
  const f = (async () => new Response('rate limited', { status: 403 })) as unknown as typeof fetch;
  expect(await loadReleases({ fetch: f, env: {} })).toEqual(empty);
  expect(readWarnings()[0]).toMatchObject({ code: 'releases.unavailable' });
  expect(readWarnings()[0].detail).toContain('403');
});

test('network error is a warning, or an error when required', async () => {
  expect(await loadReleases({ fetch: noFetch, env: {} })).toEqual(empty);
  await expect(loadReleases({ fetch: noFetch, env: { FK_REQUIRE_RELEASE: '1' } })).rejects.toThrow(
    /Latest release not readable/,
  );
});

test('GITHUB_TOKEN is sent as a bearer token and never logged', async () => {
  let seen: Headers | undefined;
  const f = (async (_u: string, init?: RequestInit) => {
    seen = new Headers(init?.headers);
    return new Response('[]');
  }) as unknown as typeof fetch;
  await loadReleases({ fetch: f, env: { GITHUB_TOKEN: 'x' } });
  expect(seen!.get('authorization')).toBe('Bearer x');
  expect(JSON.stringify(readWarnings())).not.toContain('Bearer');
});
