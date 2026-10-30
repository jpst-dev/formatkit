import { expect, test } from 'vitest';
import { shouldRenderDefaultHero } from '../src/lib/hero';

test('an empty or missing hero renders nothing (the home draws its own)', () => {
  expect(shouldRenderDefaultHero(undefined)).toBe(false);
  expect(shouldRenderDefaultHero({})).toBe(false);
  expect(shouldRenderDefaultHero({ actions: [] })).toBe(false);
});

test('a hero with a title, tagline, image or actions renders (404 splash)', () => {
  expect(shouldRenderDefaultHero({ title: '404', tagline: 'x', actions: [] })).toBe(true);
  expect(shouldRenderDefaultHero({ tagline: 'x' })).toBe(true);
  expect(shouldRenderDefaultHero({ image: { file: 'a.png' } })).toBe(true);
  expect(shouldRenderDefaultHero({ actions: [{ text: 'a', link: '/' }] })).toBe(true);
});
