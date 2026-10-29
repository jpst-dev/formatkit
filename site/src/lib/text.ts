import ptBR from '../content/i18n/pt-BR.json';
import en from '../content/i18n/en.json';
import { warn } from './warnings';

export type SiteLocale = 'pt-BR' | 'en';

const strings: Record<SiteLocale, Record<string, string>> = { 'pt-BR': ptBR, en };

/** Starlight gives `undefined` or `'root'` for the root (pt-BR) locale. */
export function localeFromAstro(locale: string | undefined): SiteLocale {
  return locale === 'en' ? 'en' : 'pt-BR';
}

/** The locale's entry, else the `en` one. */
export function pickText<T>(text: Record<string, T>, locale: SiteLocale): T {
  return locale in text ? text[locale] : text.en;
}

/** UI string lookup: locale, then en, then the key itself (with a warning). */
export function t(locale: SiteLocale, key: string): string {
  const own = strings[locale][key];
  if (own !== undefined) return own;
  const fallback = strings.en[key];
  if (fallback !== undefined) return fallback;
  warn('string.missing', key);
  return key;
}
