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
