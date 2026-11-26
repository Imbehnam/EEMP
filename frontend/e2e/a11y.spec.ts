import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

import { ADMIN, signIn, WEB } from './helpers';

/**
 * Automated accessibility gate (axe-core, WCAG 2.0/2.1/2.2 A + AA rules) for key pages in an RTL
 * and an LTR locale, light and dark themes. Fails on any serious or critical violation.
 */
const tags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

async function expectNoSeriousViolations(page: Page, label: string) {
  await page.waitForLoadState('networkidle');
  const results = await new AxeBuilder({ page }).withTags(tags).analyze();
  const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  const summary = serious.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(' ')).slice(0, 5).join(' | ')}`);
  expect(summary, `${label}: axe serious/critical violations`).toEqual([]);
}

for (const locale of ['fa', 'en'] as const) {
  for (const theme of ['light', 'dark'] as const) {
    test.describe(`a11y ${locale} ${theme}`, () => {
      test.beforeEach(async ({ context }) => {
        for (const url of [ADMIN, WEB]) await context.addCookies([{ name: 'eemp_theme', value: theme, url }]);
      });

      test('admin: login, dashboard, list, form', async ({ page }) => {
        await page.goto(`${ADMIN}/${locale}/login`);
        await expectNoSeriousViolations(page, 'login');
        await signIn(page, ADMIN, locale, 'demo', 'demo.admin');
        await expect(page.getByTestId('kpi-students')).toBeVisible();
        await expectNoSeriousViolations(page, 'dashboard');
        await page.goto(`${ADMIN}/${locale}/people/students`);
        await expect(page.getByRole('table')).toBeVisible();
        await expect(page.getByText('ST-1001')).toBeVisible();
        await expectNoSeriousViolations(page, 'students list');
        await page.goto(`${ADMIN}/${locale}/people/students/new`);
        await expect(page.locator('form')).toBeVisible();
        await expectNoSeriousViolations(page, 'student form');
      });

      test('portal home', async ({ page }) => {
        await signIn(page, WEB, locale, 'demo', 'student.reza');
        await expectNoSeriousViolations(page, 'portal home');
      });
    });
  }
}
