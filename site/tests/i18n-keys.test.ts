import { expect, test } from 'vitest';
import en from '../src/content/i18n/en.json';
import ptBR from '../src/content/i18n/pt-BR.json';

test('en.json and pt-BR.json have exactly the same keys', () => {
  expect(Object.keys(ptBR).sort()).toEqual(Object.keys(en).sort());
});
