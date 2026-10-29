import { defineCollection } from 'astro:content';
import { z } from 'astro/zod';
import { docsLoader, i18nLoader } from '@astrojs/starlight/loaders';
import { releasesLoader } from './loaders/releases';
import { knownIssuesLoader } from './loaders/knownIssues';
import { programsLoader } from './loaders/programs';
import { cleanupLoader } from './loaders/cleanup';
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
  knownIssues: defineCollection({
    loader: knownIssuesLoader(),
    schema: z.looseObject({
      id: z.string(),
      severity: z.string(),
      text: z.record(z.string(), z.object({ title: z.string(), breaks: z.string(), fix: z.string() })),
    }),
  }),
  programs: defineCollection({
    loader: programsLoader(),
    schema: z.looseObject({
      id: z.string(),
      category: z.string(),
      source: z.string(),
      wingetId: z.string().optional(),
      text: z.record(z.string(), z.object({ name: z.string(), description: z.string() })),
    }),
  }),
  cleanup: defineCollection({
    loader: cleanupLoader(),
    schema: z.looseObject({
      id: z.string(),
      category: z.string(),
      level: z.string(),
      scope: z.string(),
      contested: z.boolean(),
      requiresReboot: z.boolean(),
      laptopSensitive: z.boolean(),
      text: z.record(z.string(), z.looseObject({ title: z.string(), what: z.string(), breaks: z.string() })),
      actions: z.array(z.looseObject({ type: z.string() })),
      evidence: z
        .object({ level: z.string(), sources: z.array(z.object({ title: z.string(), url: z.string() })) })
        .nullable(),
      order: z.number(),
    }),
  }),
  i18n: defineCollection({
    loader: i18nLoader(),
    schema: i18nSchema({ extend: z.object({}).catchall(z.string()) }),
  }),
};
