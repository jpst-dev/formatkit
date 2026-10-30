import type { CleanupItem } from '../loaders/cleanup';
import { KNOWN_LEVELS } from './labels';

/** Sentences for "When to use": the level, then the flags (spec 6). */
export function whenToUse(item: CleanupItem, t: (key: string) => string): string[] {
  const known = (KNOWN_LEVELS as readonly string[]).includes(item.level);
  const out = [t(item.category === 'developer' ? 'when.developer' : known ? `when.${item.level}` : 'when.unknown')];
  if (item.contested) out.push(t('when.contested'));
  if (item.laptopSensitive) out.push(t('when.laptopSensitive'));
  return out;
}
