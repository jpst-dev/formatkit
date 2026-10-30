import { expect, test } from 'vitest';
import { levelTone } from '../src/lib/badges';

test('level tone rule', () => {
  expect(levelTone('safe')).toBe('green');
  expect(levelTone('aggressive')).toBe('amber');
  expect(levelTone('balanced')).toBe('plain');
  expect(levelTone('other')).toBe('plain');
});
