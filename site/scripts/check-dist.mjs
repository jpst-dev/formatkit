import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { assertBuilt, checkLocaleParity, findExternalRefs, findUnbasedLinks, findUnknownCommands } from './dist-checks.mjs';

const siteDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const docsDir = join(siteDir, 'src', 'content', 'docs');
const distDir = join(siteDir, 'dist');
const BASE = '/formatkit';
const OWN_ORIGIN = 'https://jpst-dev.github.io';

const walk = (dir, exts) =>
  readdirSync(dir, { withFileTypes: true, recursive: true })
    .filter((e) => e.isFile() && exts.some((x) => e.name.endsWith(x)))
    .map((e) => join(e.parentPath, e.name));

const rel = (root, f) => relative(root, f).replace(/\\/g, '/');
const problems = [];

const docs = walk(docsDir, ['.mdx']);
for (const p of checkLocaleParity(docs.map((f) => rel(docsDir, f)))) problems.push(`missing locale counterpart: ${p}`);

const readme = readFileSync(join(siteDir, '..', 'README.md'), 'utf8');
for (const f of docs) {
  for (const c of findUnknownCommands(readFileSync(f, 'utf8'), readme)) {
    problems.push(`${rel(docsDir, f)}: unknown command "formatkit ${c}"`);
  }
}

const distFiles = existsSync(distDir) ? walk(distDir, ['.html', '.css']) : [];
problems.push(...assertBuilt(distFiles.filter((f) => f.endsWith('.html')).length));
for (const f of distFiles) {
  const text = readFileSync(f, 'utf8');
  const name = rel(distDir, f);
  for (const r of findExternalRefs(name, text, OWN_ORIGIN)) problems.push(`external reference: ${r}`);
  if (f.endsWith('.html')) for (const l of findUnbasedLinks(text, BASE)) problems.push(`${name}: link without base: ${l}`);
}

for (const p of problems) console.error(p);
console.log(problems.length ? `${problems.length} problem(s)` : 'dist checks passed');
process.exit(problems.length ? 1 : 0);
