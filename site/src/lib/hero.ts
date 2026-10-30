export interface HeroLike {
  title?: string;
  tagline?: string;
  image?: unknown;
  actions?: unknown[];
}

// The home keeps `hero: {}` and draws its own hero; any other splash page (the 404)
// has its content in the hero, so Starlight's default Hero must render it.
export function shouldRenderDefaultHero(hero: HeroLike | undefined): boolean {
  if (!hero) return false;
  return Boolean(hero.title || hero.tagline || hero.image || (hero.actions?.length ?? 0) > 0);
}
