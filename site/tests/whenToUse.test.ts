import { expect, test } from 'vitest';
import { whenToUse } from '../src/lib/whenToUse';
import type { CleanupItem } from '../src/loaders/cleanup';

const base = { level: 'balanced', contested: true, laptopSensitive: true } as CleanupItem;
const tt = (k: string) => `T:${k}`;

test('level sentence, then contested, then laptopSensitive', () => {
  expect(whenToUse(base, tt)).toEqual(['T:when.balanced', 'T:when.contested', 'T:when.laptopSensitive']);
});

test('unknown level uses when.unknown; flags off add nothing', () => {
  expect(whenToUse({ ...base, level: 'weird', contested: false, laptopSensitive: false }, tt)).toEqual(['T:when.unknown']);
  expect(whenToUse({ ...base, level: 'safe', contested: false, laptopSensitive: false }, tt)).toEqual(['T:when.safe']);
});

test('developer items say they are opt-in through include, instead of the level sentence', () => {
  const dev = { ...base, category: 'developer', level: 'safe', contested: false, laptopSensitive: false } as CleanupItem;
  expect(whenToUse(dev, tt)).toEqual(['T:when.developer']);
  expect(whenToUse({ ...dev, contested: true }, tt)).toEqual(['T:when.developer', 'T:when.contested']);
});

test('opt-in items add that they are applied only through include', () => {
  const item = { ...base, category: 'power', contested: false, laptopSensitive: true, optIn: true } as CleanupItem;
  expect(whenToUse(item, tt)).toEqual(['T:when.balanced', 'T:when.optIn', 'T:when.laptopSensitive']);
});
