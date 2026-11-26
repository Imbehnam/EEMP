'use client';

import { currencyDecimals, parseAmount, type AmountParse } from '@eemp/api-client';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Checkbox, TextField, type TextFieldProps } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { forwardRef, useCallback, useEffect, useMemo, useState, useSyncExternalStore } from 'react';

const UNIT_KEY = 'eemp.moneyUnit';
const listeners = new Set<() => void>();

function readUnit(): 'rial' | 'toman' {
  if (typeof window === 'undefined') return 'rial';
  try {
    return window.localStorage.getItem(UNIT_KEY) === 'toman' ? 'toman' : 'rial';
  } catch {
    return 'rial';
  }
}

/** Display-only preference: show IRR amounts in tomans (÷10). Stored per browser; amounts are always sent in rials. */
export function useMoneyUnit() {
  const unit = useSyncExternalStore(
    (cb) => {
      listeners.add(cb);
      return () => listeners.delete(cb);
    },
    readUnit,
    () => 'rial' as const,
  );
  const setUnit = useCallback((next: 'rial' | 'toman') => {
    try {
      window.localStorage.setItem(UNIT_KEY, next);
    } catch {
      /* private mode: keep the default */
    }
    listeners.forEach((l) => l());
  }, []);
  return { unit, setUnit };
}

/**
 * Formats a server amount in the active locale (Persian digits for fa) with the translated currency unit.
 * IRR is shown in whole rials, or in tomans when the user chose so (display only).
 */
export function useMoney() {
  const format = useLocaleFormat();
  const t = useTranslations('finance.money');
  const { unit } = useMoneyUnit();
  return useMemo(() => {
    const parts = (amount: number, currency: string) => {
      const toman = unit === 'toman' && currency === 'IRR';
      const value = toman ? amount / 10 : amount;
      const decimals = toman ? (Number.isInteger(value) ? 0 : 1) : currencyDecimals(currency);
      return {
        value: format.number(value, { minimumFractionDigits: decimals, maximumFractionDigits: decimals }),
        unit: t('unit', { currency: toman ? 'IRT' : currency }),
      };
    };
    return {
      /** "۱۲٬۰۰۰٬۰۰۰ ریال" / "12,000,000 rials". */
      format: (amount: number | null | undefined, currency: string) => {
        if (amount === null || amount === undefined) return '';
        const p = parts(amount, currency);
        return t('amount', { amount: p.value, unit: p.unit });
      },
      /** Number only (for table cells whose header names the unit). */
      number: (amount: number, currency: string) => parts(amount, currency).value,
      unitLabel: (currency: string) => parts(0, currency).unit,
    };
  }, [format, t, unit]);
}

/** Formatted amount; `data-amount` keeps the raw value for tests and copy. */
export function Money({ amount, currency, strong, testId }: { amount: number | null | undefined; currency: string; strong?: boolean; testId?: string }) {
  const money = useMoney();
  const text = money.format(amount, currency);
  return strong ? (
    <strong className="eemp-money" data-amount={amount ?? ''} data-testid={testId}>{text}</strong>
  ) : (
    <span className="eemp-money" data-amount={amount ?? ''} data-testid={testId}>{text}</span>
  );
}

export function MoneyUnitToggle({ currency }: { currency: string }) {
  const t = useTranslations('finance.money');
  const { unit, setUnit } = useMoneyUnit();
  if (currency !== 'IRR') return null;
  return (
    <Checkbox
      label={t('showToman')}
      hint={t('tomanHint')}
      checked={unit === 'toman'}
      onChange={(e) => setUnit(e.target.checked ? 'toman' : 'rial')}
      data-testid="toman-toggle"
    />
  );
}

export type AmountFieldProps = Omit<TextFieldProps, 'value' | 'onChange' | 'type'> & {
  currency: string;
  /** The last valid amount, or null. */
  onAmount: (value: number | null, parsed: AmountParse) => void;
  initial?: number | null;
};

/**
 * Amount input: Persian/Latin digits, optional separators, validated against the currency precision on blur. The
 * value sent to the API is the parsed number in the stored currency (rials), whatever the display preference.
 */
export const AmountField = forwardRef<HTMLInputElement, AmountFieldProps>(function AmountField({ currency, onAmount, initial, error, hint, ...rest }, ref) {
  const t = useTranslations('finance.money');
  const [text, setText] = useState(initial === null || initial === undefined ? '' : String(initial));
  const [problem, setProblem] = useState<string | undefined>();
  useEffect(() => {
    if (initial !== undefined && initial !== null) setText(String(initial));
  }, [initial]);
  const check = (value: string, show: boolean) => {
    const parsed = parseAmount(value, currency);
    onAmount(parsed.ok ? parsed.value : null, parsed);
    if (show) setProblem(parsed.ok || parsed.reason === 'empty' ? undefined : t(parsed.reason));
  };
  return (
    <TextField
      ref={ref}
      inputMode="decimal"
      dir="ltr"
      autoComplete="off"
      value={text}
      hint={hint ?? t('hintRials')}
      error={error ?? problem}
      onChange={(e) => {
        setText(e.target.value);
        check(e.target.value, false);
      }}
      onBlur={(e) => check(e.target.value, true)}
      {...rest}
    />
  );
});
