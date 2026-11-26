import { defineConfig } from '@playwright/test';

import base from './playwright.config';

/** Design review screenshots; see e2e-shots/capture.spec.ts. */
export default defineConfig({ ...base, testDir: './e2e-shots', retries: 0, reporter: 'list' });
