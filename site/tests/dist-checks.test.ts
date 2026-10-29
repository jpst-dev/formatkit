import { expect, test } from 'vitest';
// @ts-expect-error plain ESM module without types
import * as checks from '../scripts/dist-checks.mjs';

const { checkLocaleParity, findExternalRefs, findUnbasedLinks, commandTokens, findUnknownCommands, assertBuilt } = checks as {
  checkLocaleParity: (p: string[]) => string[];
  findExternalRefs: (f: string, t: string, o: string) => string[];
  findUnbasedLinks: (t: string, b: string) => string[];
  commandTokens: (l: string) => string[][];
  findUnknownCommands: (c: string, r: string) => string[];
  assertBuilt: (n: number) => string[];
};

const NL = String.fromCharCode(10);
const OWN = 'https://jpst-dev.github.io';
const readme = [
  '| `formatkit hardware` | x |',
  '| `formatkit drivers scan` | x |',
  '| `formatkit run --profile <file> [--dry-run]` | x |',
  '| `formatkit cleanup undo <ids...> \| --all` | x |',
].join('\n');

test('parity flags a pt-BR page without its English counterpart', () => {
  expect(checkLocaleParity(['guide/before.mdx', 'en/guide/after.mdx', 'guide/after.mdx'])).toEqual([
    'guide/before.mdx',
  ]);
});

test('parity flags an English page without its pt-BR counterpart', () => {
  expect(checkLocaleParity(['en/faq.mdx'])).toEqual(['en/faq.mdx']);
});

test('parity passes when both exist', () => {
  expect(checkLocaleParity(['faq.mdx', 'en/faq.mdx'])).toEqual([]);
});

test('external script is flagged', () => {
  expect(findExternalRefs('a.html', '<script src="https://x.com/a.js"></script>', OWN)).toEqual([
    'a.html: https://x.com/a.js',
  ]);
});

test('css url() with a third-party host is flagged', () => {
  expect(findExternalRefs('a.css', '@font-face{src:url(https://fonts.gstatic.com/f.woff2)}', OWN)).toHaveLength(1);
});

test('anchor to an external site is ignored', () => {
  expect(findExternalRefs('a.html', '<a href="https://github.com">x</a>', OWN)).toEqual([]);
});

test('protocol-relative img is flagged', () => {
  expect(findExternalRefs('a.html', '<img src="//cdn.x.com/i.png">', OWN)).toHaveLength(1);
});

test('own-origin canonical link is not flagged, third-party stylesheet is', () => {
  expect(findExternalRefs('a.html', '<link rel="canonical" href="https://jpst-dev.github.io/formatkit/">', OWN)).toEqual([]);
  expect(findExternalRefs('a.html', '<link rel="stylesheet" href="https://fonts.googleapis.com/x">', OWN)).toHaveLength(1);
});

test('unbased root-relative link is flagged, based passes', () => {
  expect(findUnbasedLinks('<a href="/download/">', '/formatkit')).toEqual(['/download/']);
  expect(findUnbasedLinks('<a href="/formatkit/download/">', '/formatkit')).toEqual([]);
  expect(findUnbasedLinks('<a href="//x.com/">', '/formatkit')).toEqual([]);
});

test('commandTokens takes up to two command words', () => {
  expect(commandTokens('formatkit cleanup undo apps.clipchamp')).toEqual([['cleanup', 'undo']]);
  expect(commandTokens('formatkit run --profile x')).toEqual([['run']]);
});

test('unknown commands are reported, README ones pass', () => {
  expect(findUnknownCommands('`formatkit drivers scna`', readme)).toEqual(['drivers scna']);
  expect(findUnknownCommands('`formatkit run --profile x`', readme)).toEqual([]);
  expect(findUnknownCommands('`formatkit cleanup undo apps.x`', readme)).toEqual([]);
});

test('css @import string form and image-set strings are flagged', () => {
  expect(findExternalRefs('a.css', '@import "https://x.com/y.css";', OWN)).toHaveLength(1);
  expect(findExternalRefs('a.css', "@import 'https://x.com/y.css';", OWN)).toHaveLength(1);
  expect(findExternalRefs('a.css', 'a{background:image-set("https://x.com/a.png" 1x, "/formatkit/b.png" 2x)}', OWN)).toHaveLength(1);
  expect(findExternalRefs('a.css', '@import "./local.css";', OWN)).toEqual([]);
});

test('prose is not scanned for commands', () => {
  expect(findUnknownCommands('O formatkit faz tudo e formatkit drivers scna no texto', readme)).toEqual([]);
});

test('inline code and fenced blocks are scanned', () => {
  expect(findUnknownCommands(['```powershell', 'formatkit drivers scna', '```'].join(NL), readme)).toEqual(['drivers scna']);
  expect(findUnknownCommands(['```powershell', 'formatkit drivers scan', '```'].join(NL), readme)).toEqual([]);
});

test('formatkit with no command word is skipped', () => {
  expect(findUnknownCommands('`formatkit --help`', readme)).toEqual([]);
});

test('double spaces are tolerated', () => {
  expect(findUnknownCommands('`formatkit  drivers   scan`', readme)).toEqual([]);
});

test('a one-word README command accepts a positional argument', () => {
  expect(findUnknownCommands('`formatkit run foo`', readme)).toEqual([]);
});

test('unknown subcommand and bare group fail', () => {
  expect(findUnknownCommands('`formatkit drivers scna`', readme)).toEqual(['drivers scna']);
  expect(findUnknownCommands('`formatkit cleanup`', readme)).toEqual(['cleanup']);
});

test('assertBuilt fails when dist has no HTML', () => {
  expect(assertBuilt(0)).toEqual(['dist has no HTML: run npm run build first']);
  expect(assertBuilt(3)).toEqual([]);
});
