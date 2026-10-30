// Renders assets/formatkit.svg into the multi-size assets/formatkit.ico used by
// formatkit.exe and FormatKitUI.exe, and copies the SVG to the site favicon.
// Usage: npm ci && npm run make (from tools/make-icon).
import { Resvg } from '@resvg/resvg-js';
import { copyFileSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));
const svg = readFileSync(`${root}assets/formatkit.svg`);
const sizes = [16, 24, 32, 48, 64, 256];

const images = sizes.map((size) =>
  new Resvg(svg, { fitTo: { mode: 'width', value: size } }).render().asPng(),
);

// ICO layout: ICONDIR (6 bytes), one ICONDIRENTRY (16 bytes) per image, then the PNG data.
const header = Buffer.alloc(6 + 16 * sizes.length);
header.writeUInt16LE(0, 0);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(sizes.length, 4);
let offset = header.length;
sizes.forEach((size, i) => {
  const entry = 6 + 16 * i;
  header.writeUInt8(size === 256 ? 0 : size, entry); // 0 means 256
  header.writeUInt8(size === 256 ? 0 : size, entry + 1);
  header.writeUInt8(0, entry + 2); // no palette
  header.writeUInt8(0, entry + 3);
  header.writeUInt16LE(1, entry + 4); // colour planes
  header.writeUInt16LE(32, entry + 6); // bits per pixel
  header.writeUInt32LE(images[i].length, entry + 8);
  header.writeUInt32LE(offset, entry + 12);
  offset += images[i].length;
});

writeFileSync(`${root}assets/formatkit.ico`, Buffer.concat([header, ...images]));
copyFileSync(`${root}assets/formatkit.svg`, `${root}site/public/favicon.svg`);
console.log(`assets/formatkit.ico: ${sizes.join(', ')} px; site/public/favicon.svg updated`);
