import { defineCollection } from 'astro:content';
import { z } from 'astro/zod';
import { docsLoader, i18nLoader } from '@astrojs/starlight/loaders';
import { releasesLoader } from './loaders/releases';
import { docsSchema, i18nSchema } from '@astrojs/starlight/schema';

export const collections = {
  docs: defineCollection({ loader: docsLoader(), schema: docsSchema() }),
  releases: defineCollection({
    loader: releasesLoader(),
    schema: z.object({
      id: z.string(),
      tag: z.string(),
      name: z.string(),
      date: z.string(),
      url: z.string(),
      prerelease: z.boolean(),
      body: z.string(),
      assets: z.array(
        z.object({ name: z.string(), url: z.string(), size: z.number(), sha256: z.string().nullable() }),
      ),
    }),
  }),
  i18n: defineCollection({
    loader: i18nLoader(),
    schema: i18nSchema({ extend: z.object({}).catchall(z.string()) }),
  }),
};
