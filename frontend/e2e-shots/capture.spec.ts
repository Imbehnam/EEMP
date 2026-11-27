import { test, type Page } from '@playwright/test';

import { ADMIN, signIn, WEB } from '../e2e/helpers';

/**
 * Design review screenshots (mock mode, fa locale). Not part of CI.
 * SHOTS_DIR=<dir> SHOTS_PREFIX=after pnpm exec playwright test -c playwright.shots.config.ts
 */
const dir = process.env.SHOTS_DIR ?? 'test-results/shots';
const prefix = process.env.SHOTS_PREFIX ?? 'shot';
const theme = process.env.SHOTS_THEME ?? 'light';

const viewports = [
  { name: 'desktop', width: 1440, height: 900 },
  { name: 'mobile', width: 390, height: 844 },
];

async function shoot(page: Page, name: string, vp: string) {
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(400);
  const suffix = theme === 'light' ? '' : `-${theme}`;
  await page.screenshot({ path: `${dir}/${prefix}-${name}-${vp}${suffix}.png`, fullPage: true });
}

for (const vp of viewports) {
  test.describe(vp.name, () => {
    test.use({ viewport: { width: vp.width, height: vp.height }, locale: 'fa-IR' });
    test.beforeEach(async ({ context }) => {
      for (const url of [ADMIN, WEB]) await context.addCookies([{ name: 'eemp_theme', value: theme, url }]);
    });

    test('admin', async ({ page }) => {
      await page.goto(`${ADMIN}/fa/login`);
      await shoot(page, 'login', vp.name);
      await signIn(page, ADMIN, 'fa', 'demo', 'demo.admin');
      await shoot(page, 'dashboard', vp.name);
      await page.goto(`${ADMIN}/fa/people/students`);
      await page.getByRole('table').waitFor();
      await shoot(page, 'list', vp.name);
      await page.goto(`${ADMIN}/fa/people/students/new`);
      await shoot(page, 'form', vp.name);
    });

    test('portal', async ({ page }) => {
      await signIn(page, WEB, 'fa', 'demo', 'student.reza');
      await shoot(page, 'portal-home', vp.name);
    });
  });
}
