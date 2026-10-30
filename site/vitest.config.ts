/// <reference types="vitest/config" />
import { getViteConfig } from 'astro/config';

export default getViteConfig({
  test: {
    include: ['tests/**/*.test.ts'],
    // tests share site/.fk-warnings.json, so files must not run in parallel
    fileParallelism: false,
  },
});
