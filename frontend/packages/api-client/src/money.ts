/**
 * Client-side money helpers. The server owns every computed amount (totals, VAT, allocation, rounding); the client
 * only parses what a user types and validates its precision before sending it (DL-059).
 */

/** Decimal places of a currency's minor unit as the backend rounds it (IRR/IRT: 0, others: ISO default, assumed 2). */
export function currencyDecimals(currency: string): number {
  const code = currency.toUpperCase();
  if (code === 'IRR' || code === 'IRT' || code === 'JPY' || code === 'KRW') return 0;
  if (['BHD', 'KWD', 'OMR', 'JOD', 'IQD', 'TND', 'LYD'].includes(code)) return 3;
  return 2;
}

const DIGITS: Record<string, string> = {};
'۰۱۲۳۴۵۶۷۸۹'.split('').forEach((d, i) => (DIGITS[d] = String(i)));
'٠١٢٣٤٥٦٧٨٩'.split('').forEach((d, i) => (DIGITS[d] = String(i)));

/** Persian/Arabic-Indic digits → ASCII. */
export function toAsciiDigits(value: string): string {
  return value.replace(/[۰-۹٠-٩]/g, (d) => DIGITS[d] ?? d);
}

export type AmountParse = { ok: true; value: number } | { ok: false; reason: 'empty' | 'invalid' | 'precision' | 'notPositive' };

/**
 * Parses a typed amount: Persian/Arabic or Latin digits, optional thousands separators (`,` `٬` `،` space) and a
 * decimal point (`.` or `٫`). Rejects more decimals than the currency allows and non-positive values.
 * Returns a JS number for the JSON body (amounts stay far below Number.MAX_SAFE_INTEGER).
 */
export function parseAmount(input: string, currency: string): AmountParse {
  const text = toAsciiDigits(input)
    .replace(/[\s,٬،\u200e\u200f]/g, '')
    .replace('٫', '.');
  if (text === '') return { ok: false, reason: 'empty' };
  if (!/^\d+(\.\d+)?$/.test(text)) return { ok: false, reason: 'invalid' };
  const decimals = text.includes('.') ? text.split('.')[1]!.replace(/0+$/, '').length : 0;
  if (decimals > currencyDecimals(currency)) return { ok: false, reason: 'precision' };
  const value = Number(text);
  if (!Number.isFinite(value) || value > Number.MAX_SAFE_INTEGER) return { ok: false, reason: 'invalid' };
  if (value <= 0) return { ok: false, reason: 'notPositive' };
  return { ok: true, value };
}

/** Random idempotency key for one user action (a retried submit of the same form reuses it). */
export function newIdempotencyKey(prefix = 'ui'): string {
  const random = globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
  return `${prefix}-${random}`;
}

/**
 * The fake development gateway's payment URL ends with the provider authority (`…/pay/<AUTHORITY>`). The portal's
 * fake gateway page needs it to report the payer's choice to the callback. Returns null for any other URL shape.
 */
export function authorityFromRedirect(redirectUrl: string): string | null {
  try {
    const url = new URL(redirectUrl);
    const last = url.pathname.split('/').filter(Boolean).pop();
    return last && /^[A-Za-z0-9_-]{4,100}$/.test(last) ? last : null;
  } catch {
    return null;
  }
}
