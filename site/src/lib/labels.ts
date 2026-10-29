import { warn } from './warnings';

export const KNOWN_LEVELS = ['safe', 'balanced', 'aggressive'] as const;
export const KNOWN_CATEGORIES = ['apps', 'privacy', 'performance', 'developer'] as const;

export function levelRank(level: string): number {
  const i = (KNOWN_LEVELS as readonly string[]).indexOf(level);
  return i === -1 ? KNOWN_LEVELS.length : i;
}

function titleCase(value: string): string {
  return value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .split(/[\s_-]+/)
    .filter(Boolean)
    .map((w) => w[0].toUpperCase() + w.slice(1))
    .join(' ');
}

export function labelFor(kind: 'category' | 'level' | 'programCategory', value: string, labels: Record<string, string>): string {
  const label = labels[`${kind}.${value}`];
  if (label !== undefined) return label;
  warn(`${kind}.unknown`, value);
  return titleCase(value);
}
