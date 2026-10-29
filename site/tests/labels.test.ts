import { beforeEach, expect, test } from 'vitest';
import { readWarnings, resetWarnings } from '../src/lib/warnings';
import { labelFor, levelRank } from '../src/lib/labels';

beforeEach(() => resetWarnings());

test('levelRank orders known levels and puts unknown last', () => {
  expect(levelRank('balanced')).toBe(1);
  expect(levelRank('extreme')).toBe(3);
});

test('labelFor title-cases unknown values and warns', () => {
  expect(labelFor('category', 'windowsSettings', {})).toBe('Windows Settings');
  expect(readWarnings()).toEqual([{ code: 'category.unknown', detail: 'windowsSettings' }]);
});

test('labelFor splits on dash and underscore', () => {
  expect(labelFor('level', 'extra-fast_mode', {})).toBe('Extra Fast Mode');
});

test('labelFor uses the label when present, without warning', () => {
  expect(labelFor('level', 'safe', { 'level.safe': 'Seguro' })).toBe('Seguro');
  expect(readWarnings()).toEqual([]);
});
