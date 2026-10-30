import { expect, test } from 'vitest';
import { readWarnings, resetWarnings, warn } from '../src/lib/warnings';

test('warn records each distinct warning once', () => {
  resetWarnings();
  warn('category.unknown', 'windowsSettings');
  warn('category.unknown', 'windowsSettings');
  warn('level.unknown', 'extreme');
  expect(readWarnings()).toEqual([
    { code: 'category.unknown', detail: 'windowsSettings' },
    { code: 'level.unknown', detail: 'extreme' },
  ]);
});
