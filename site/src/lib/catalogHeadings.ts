import type { CleanupItem } from '../loaders/cleanup';
import { sortForIndex } from '../loaders/cleanup';
import { labelFor } from './labels';

export interface Heading {
  depth: number;
  slug: string;
  text: string;
}

/** Sections of an item page, in order. The slug is the same in every locale so links survive a language switch. */
const ITEM_SECTIONS = ['what', 'breaks', 'when', 'evidence', 'undo', 'technical'] as const;

export function itemHeadings(tr: (key: string) => string): Heading[] {
  return ITEM_SECTIONS.map((s) => ({ depth: 2, slug: s, text: tr(`item.${s}`) }));
}

/** One heading per category of the catalog index; the slug is the category id. */
export function catalogHeadings(items: CleanupItem[], labels: Record<string, string>): Heading[] {
  return [...sortForIndex(items).keys()].map((c) => ({ depth: 2, slug: c, text: labelFor('category', c, labels) }));
}

/** Starlight's table of contents with headings rendered by components added after its overview entry. */
export function withHeadings<T extends Heading & { children: unknown[] }>(toc: T[], headings: Heading[]): (T | (Heading & { children: [] }))[] {
  return [...toc, ...headings.map((h) => ({ ...h, children: [] as [] }))];
}
