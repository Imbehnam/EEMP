import { existsSync } from 'node:fs';

import { chromium } from '@playwright/test';

/**
 * Launch options for Chromium: Playwright's bundled browser when it is installed (CI), otherwise a system Chromium
 * (EEMP_CHROMIUM_PATH, default /usr/bin/chromium) for sandboxes where `playwright install` is not possible.
 */
export function chromiumLaunchOptions(): { executablePath?: string } {
  let bundled = '';
  try {
    bundled = chromium.executablePath();
  } catch {
    bundled = '';
  }
  if (bundled && existsSync(bundled)) return {};
  const system = process.env.EEMP_CHROMIUM_PATH ?? '/usr/bin/chromium';
  return existsSync(system) ? { executablePath: system } : {};
}
