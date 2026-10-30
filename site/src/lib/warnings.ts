import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

export type Warning = { code: string; detail: string };

/** site/.fk-warnings.json, shared by every module of one build. */
export const warningsFile = fileURLToPath(new URL('../../.fk-warnings.json', import.meta.url));

export function readWarnings(): Warning[] {
  if (!existsSync(warningsFile)) return [];
  try {
    return JSON.parse(readFileSync(warningsFile, 'utf8')) as Warning[];
  } catch {
    return [];
  }
}

export function resetWarnings(): void {
  rmSync(warningsFile, { force: true });
}

/** Prints and records a warning once per distinct (code, detail) pair. */
export function warn(code: string, detail: string): void {
  const all = readWarnings();
  if (all.some((w) => w.code === code && w.detail === detail)) return;
  all.push({ code, detail });
  writeFileSync(warningsFile, JSON.stringify(all, null, 2));
  console.warn(`[fk-warning] ${code}: ${detail}`);
}
