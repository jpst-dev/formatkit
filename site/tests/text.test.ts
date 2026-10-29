import { beforeEach, expect, test, vi } from 'vitest';
import { readWarnings, resetWarnings } from '../src/lib/warnings';

vi.mock('../src/content/i18n/pt-BR.json', () => ({ default: { both: 'ambos-pt', 'pt.only': 'so-pt' } }));
vi.mock('../src/content/i18n/en.json', () => ({ default: { both: 'both-en', 'en.only': 'only-en' } }));

import { localeFromAstro, pickText, t } from '../src/lib/text';

beforeEach(() => resetWarnings());

test('pickText returns the locale entry, else en', () => {
  expect(pickText({ en: 'a', 'pt-BR': 'b' }, 'pt-BR')).toBe('b');
  expect(pickText({ en: 'a' }, 'pt-BR')).toBe('a');
});

test('localeFromAstro maps undefined/root to pt-BR and en to en', () => {
  expect(localeFromAstro(undefined)).toBe('pt-BR');
  expect(localeFromAstro('root')).toBe('pt-BR');
  expect(localeFromAstro('en')).toBe('en');
});

test('t returns the locale string when present in both', () => {
  expect(t('pt-BR', 'both')).toBe('ambos-pt');
  expect(t('en', 'both')).toBe('both-en');
  expect(readWarnings()).toEqual([]);
});

test('t falls back to en when the locale lacks the key', () => {
  expect(t('pt-BR', 'en.only')).toBe('only-en');
  expect(readWarnings()).toEqual([]);
});

test('t returns the key and warns when missing everywhere', () => {
  expect(t('pt-BR', 'nope')).toBe('nope');
  expect(readWarnings()).toEqual([{ code: 'string.missing', detail: 'nope' }]);
});
