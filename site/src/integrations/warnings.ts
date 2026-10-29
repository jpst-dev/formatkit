import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import type { AstroIntegration } from 'astro';
import { readWarnings, resetWarnings } from '../lib/warnings';

export function fkWarnings(): AstroIntegration {
  return {
    name: 'fk-warnings',
    hooks: {
      'astro:config:setup': () => {
        resetWarnings();
      },
      'astro:build:done': ({ dir }) => {
        const out = fileURLToPath(new URL('build-warnings.json', dir));
        writeFileSync(out, JSON.stringify(readWarnings(), null, 2));
      },
    },
  };
}
