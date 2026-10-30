import type { CleanupItem } from '../loaders/cleanup';

type CatalogPath = { params: { locale: string | undefined; id: string }; props: { item: CleanupItem } };

/** One path per item and language: the root (pt-BR) page and the `en` page. */
export function pathsFor(items: CleanupItem[]): CatalogPath[] {
  return items.flatMap((item) => [
    { params: { locale: undefined, id: item.id }, props: { item } },
    { params: { locale: 'en', id: item.id }, props: { item } },
  ]);
}

/** Site-internal link with exactly one slash between base, locale prefix and path. */
export function siteHref(base: string, locale: 'en' | 'pt-BR', path: string): string {
  const parts = [base, locale === 'en' ? 'en' : '', path].flatMap((p) => p.split('/')).filter(Boolean);
  return '/' + parts.join('/') + '/';
}
