// Pure check functions used by check-dist.mjs and tests. Plain ESM so it runs without a build.

export function checkLocaleParity(docPaths) {
  const set = new Set(docPaths.map((p) => p.replaceAll(String.fromCharCode(92), '/')));
  const missing = [];
  for (const p of set) {
    const counterpart = p.startsWith('en/') ? p.slice(3) : `en/${p}`;
    if (!set.has(counterpart)) missing.push(p);
  }
  return missing;
}

const isExternal = (url, ownOrigin) => {
  const u = url.trim();
  if (!/^(https?:)?\/\//i.test(u)) return false;
  return !(ownOrigin && (u === ownOrigin || u.startsWith(ownOrigin + '/')));
};

export function findExternalRefs(file, text, ownOrigin) {
  const found = [];
  const add = (url) => {
    if (isExternal(url, ownOrigin)) found.push(`${file}: ${url.trim()}`);
  };
  for (const m of text.matchAll(/<(script|iframe|source|img|link)\b[^>]*>/gi)) {
    const tag = m[0];
    const name = m[1].toLowerCase();
    const attr = name === 'link' ? 'href' : 'src';
    const a = tag.match(new RegExp(String.raw`\s${attr}\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))`, 'i'));
    if (a) add(a[1] ?? a[2] ?? a[3]);
    if (name === 'img' || name === 'source') {
      const s = tag.match(/\ssrcset\s*=\s*(?:"([^"]*)"|'([^']*)')/i);
      if (s) for (const part of (s[1] ?? s[2]).split(',')) add(part.trim().split(/\s+/)[0]);
    }
  }
  for (const m of text.matchAll(/url\(\s*(?:"([^"]*)"|'([^']*)'|([^)\s]*))\s*\)/gi)) add(m[1] ?? m[2] ?? m[3]);
  return found;
}

export function findUnbasedLinks(text, base) {
  const bad = [];
  for (const m of text.matchAll(/\shref\s*=\s*(?:"([^"]*)"|'([^']*)')/gi)) {
    const href = m[1] ?? m[2];
    if (href.startsWith('/') && !href.startsWith('//') && !href.startsWith(base + '/')) bad.push(href);
  }
  return bad;
}

export function commandTokens(line) {
  const result = [];
  const re = /formatkit /g;
  let m;
  while ((m = re.exec(line))) {
    const tokens = [];
    for (const word of line.slice(m.index + m[0].length).split(/\s+/)) {
      const clean = word.replace(/[`.,;:)]+$/, '');
      if (tokens.length < 2 && /^[a-z][a-z-]*$/.test(clean)) tokens.push(clean);
      else break;
      if (clean !== word) break;
    }
    result.push(tokens);
  }
  return result;
}

export function findUnknownCommands(contentText, readmeText) {
  const known = new Set();
  for (const line of readmeText.split(/\r?\n/)) {
    if (line.startsWith('| `formatkit')) for (const t of commandTokens(line)) known.add(t.join(' '));
  }
  const unknown = new Set();
  for (const line of contentText.split(/\r?\n/)) {
    for (const t of commandTokens(line)) {
      const key = t.join(' ');
      if (!known.has(key)) unknown.add(key);
    }
  }
  return [...unknown];
}
