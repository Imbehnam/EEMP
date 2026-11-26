// @vitest-environment jsdom
import { ThemeProvider } from '@emotion/react';
import { tokens } from '@eemp/ui';
import { act, cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { NextIntlClientProvider } from 'next-intl';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { PERMISSIONS } from '../../api-client/src/mock/seed';
import { permissionResource } from '../../api-client/src/permissions';
import ar from '../../i18n/messages/ar.json';
import en from '../../i18n/messages/en.json';
import fa from '../../i18n/messages/fa.json';
import tr from '../../i18n/messages/tr.json';
// Direct module imports: the finance barrel pulls next-intl navigation, which needs the Next.js runtime.
import { InvoiceStatusBadge } from '../src/finance/InvoiceView';
import { AmountField, Money, MoneyUnitToggle } from '../src/finance/money';

const catalogs = { fa, en, tr, ar } as const;

function wrap(locale: keyof typeof catalogs, ui: ReactNode) {
  return render(
    <NextIntlClientProvider locale={locale} messages={catalogs[locale]} timeZone="Asia/Tehran">
      <ThemeProvider theme={tokens}>{ui}</ThemeProvider>
    </NextIntlClientProvider>,
  );
}

beforeEach(() => window.localStorage.clear());
afterEach(() => cleanup());

describe('Money', () => {
  it('fa: whole rials with Persian digits and the rial unit', () => {
    wrap('fa', <Money amount={12_000_000} currency="IRR" testId="m" />);
    expect(screen.getByTestId('m')).toHaveTextContent('۱۲٬۰۰۰٬۰۰۰ ریال');
    expect(screen.getByTestId('m')).toHaveAttribute('data-amount', '12000000');
  });

  it('en/tr/ar: locale digits and translated unit', () => {
    wrap('en', <Money amount={5_000_000} currency="IRR" testId="en" />);
    expect(screen.getByTestId('en')).toHaveTextContent('5,000,000 rials');
    wrap('tr', <Money amount={5_000_000} currency="IRR" testId="tr" />);
    expect(screen.getByTestId('tr')).toHaveTextContent('5.000.000 riyal');
    wrap('ar', <Money amount={5_000_000} currency="IRR" testId="ar" />);
    expect(screen.getByTestId('ar').textContent).toContain('ريال');
  });

  it('optional toman display divides by ten (display only)', async () => {
    wrap('fa', <><MoneyUnitToggle currency="IRR" /><Money amount={12_000_005} currency="IRR" testId="m" /></>);
    await userEvent.click(screen.getByRole('checkbox'));
    expect(screen.getByTestId('m')).toHaveTextContent('۱٬۲۰۰٬۰۰۰٫۵ تومان');
    expect(screen.getByTestId('m')).toHaveAttribute('data-amount', '12000005');
  });
});

describe('AmountField', () => {
  it('accepts Persian digits and reports precision errors on blur', async () => {
    const onAmount = vi.fn();
    wrap('fa', <AmountField label="مبلغ" currency="IRR" onAmount={onAmount} />);
    const input = screen.getByLabelText(/مبلغ/);
    await userEvent.type(input, '۲٬۵۰۰٬۰۰۰');
    expect(onAmount).toHaveBeenLastCalledWith(2_500_000, { ok: true, value: 2_500_000 });
    await userEvent.clear(input);
    await userEvent.type(input, '10.5');
    act(() => input.blur());
    expect(screen.getByText('مبلغ را به ریال و بدون اعشار وارد کنید.')).toBeInTheDocument();
  });
});

describe('finance labels', () => {
  it('translates invoice statuses in every locale', () => {
    wrap('fa', <InvoiceStatusBadge status="partiallyPaid" overdue />);
    expect(screen.getByText('پرداخت جزئی')).toBeInTheDocument();
    expect(screen.getByText('سررسید گذشته')).toBeInTheDocument();
  });

  it('every permission resource (incl. finance.* and workflow.*) has a role-matrix label in every locale', () => {
    const resources = [...new Set(PERMISSIONS.map((p) => permissionResource(p.code).replace(/\./g, '_')))];
    for (const [locale, catalog] of Object.entries(catalogs)) {
      const select = catalog.roles.module;
      for (const r of resources) expect(select, `${locale}:${r}`).toMatch(new RegExp(`(^|[ {])${r} \\{`));
    }
  });
});
