import { defineRouteMiddleware } from '@astrojs/starlight/route-data';
import { getCollection } from 'astro:content';
import type { CleanupItem } from './loaders/cleanup';
import { catalogHeadings, withHeadings } from './lib/catalogHeadings';
import { labelsFor, localeFromAstro } from './lib/text';

// The catalog index draws its category headings in CatalogIndex, which Starlight's
// table of contents cannot see: add them here so "On this page" lists the categories.
export const onRequest = defineRouteMiddleware(async (context) => {
  const route = context.locals.starlightRoute;
  if (!route.toc || !['catalog', 'en/catalog'].includes(route.entry.id)) return;
  const items = (await getCollection('cleanup')).map((e) => e.data as CleanupItem);
  route.toc.items = withHeadings(route.toc.items, catalogHeadings(items, labelsFor(localeFromAstro(route.locale))));
});
