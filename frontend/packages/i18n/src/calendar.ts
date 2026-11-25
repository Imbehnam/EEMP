/**
 * Calendar-date helpers for academic dates (OD-020): the API exchanges ISO Gregorian `yyyy-MM-dd`;
 * the UI shows and accepts Jalali (Solar Hijri) dates for `fa` and Gregorian dates otherwise.
 * Pure arithmetic (no Date/time zone involved), so a calendar date never shifts by a day.
 * Jalali conversion follows the 33-year-cycle algorithm by Borkowski (as used by jalaali-js), and the
 * tests cross-check it against Intl's `persian` calendar.
 */
import { getMetadata } from './locales';

export interface YMD {
  year: number;
  month: number;
  day: number;
}

export type DateCalendar = 'persian' | 'gregory';

const div = (a: number, b: number) => Math.trunc(a / b);
const mod = (a: number, b: number) => a - Math.trunc(a / b) * b;

const BREAKS = [-61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178];

function jalCal(jy: number): { leap: number; gy: number; march: number } {
  const gy = jy + 621;
  let leapJ = -14;
  let jp = BREAKS[0]!;
  let jump = 0;
  if (jy < jp || jy >= BREAKS[BREAKS.length - 1]!) throw new RangeError(`Jalali year out of range: ${jy}`);
  for (let i = 1; i < BREAKS.length; i += 1) {
    const jm = BREAKS[i]!;
    jump = jm - jp;
    if (jy < jm) break;
    leapJ = leapJ + div(jump, 33) * 8 + div(mod(jump, 33), 4);
    jp = jm;
  }
  let n = jy - jp;
  leapJ = leapJ + div(n, 33) * 8 + div(mod(n, 33) + 3, 4);
  if (mod(jump, 33) === 4 && jump - n === 4) leapJ += 1;
  const leapG = div(gy, 4) - div((div(gy, 100) + 1) * 3, 4) - 150;
  const march = 20 + leapJ - leapG;
  if (jump - n < 6) n = n - jump + div(jump + 4, 33) * 33;
  let leap = mod(mod(n + 1, 33) - 1, 4);
  if (leap === -1) leap = 4;
  return { leap, gy, march };
}

/** Gregorian date → Julian day number. */
function g2d(gy: number, gm: number, gd: number): number {
  const d = div((gy + div(gm - 8, 6) + 100100) * 1461, 4) + div(153 * mod(gm + 9, 12) + 2, 5) + gd - 34840408;
  return d - div(div(gy + 100100 + div(gm - 8, 6), 100) * 3, 4) + 752;
}

/** Julian day number → Gregorian date. */
function d2g(jdn: number): YMD {
  let j = 4 * jdn + 139361631;
  j = j + div(div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908;
  const i = div(mod(j, 1461), 4) * 5 + 308;
  const day = div(mod(i, 153), 5) + 1;
  const month = mod(div(i, 153), 12) + 1;
  const year = div(j, 1461) - 100100 + div(8 - month, 6);
  return { year, month, day };
}

function j2d(jy: number, jm: number, jd: number): number {
  const r = jalCal(jy);
  return g2d(r.gy, 3, r.march) + (jm - 1) * 31 - div(jm, 7) * (jm - 7) + jd - 1;
}

function d2j(jdn: number): YMD {
  const gy = d2g(jdn).year;
  let jy = gy - 621;
  const r = jalCal(jy);
  const start = g2d(gy, 3, r.march);
  let k = jdn - start;
  if (k >= 0) {
    if (k <= 185) return { year: jy, month: 1 + div(k, 31), day: mod(k, 31) + 1 };
    k -= 186;
  } else {
    jy -= 1;
    k += 179;
    if (r.leap === 1) k += 1;
  }
  return { year: jy, month: 7 + div(k, 30), day: mod(k, 30) + 1 };
}

export function isJalaliLeapYear(jy: number): boolean {
  return jalCal(jy).leap === 0;
}

export function toJalali(g: YMD): YMD {
  return d2j(g2d(g.year, g.month, g.day));
}

export function fromJalali(j: YMD): YMD {
  return d2g(j2d(j.year, j.month, j.day));
}

function isGregorianLeap(y: number) {
  return (y % 4 === 0 && y % 100 !== 0) || y % 400 === 0;
}

export function monthLength(calendar: DateCalendar, year: number, month: number): number {
  if (calendar === 'persian') {
    if (month <= 6) return 31;
    if (month <= 11) return 30;
    return isJalaliLeapYear(year) ? 30 : 29;
  }
  return [31, isGregorianLeap(year) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1]!;
}

export function isValidYmd(calendar: DateCalendar, v: YMD): boolean {
  if (![v.year, v.month, v.day].every(Number.isInteger)) return false;
  if (v.month < 1 || v.month > 12 || v.day < 1) return false;
  if (calendar === 'persian' && (v.year < 1 || v.year > 3000)) return false;
  if (calendar === 'gregory' && (v.year < 622 || v.year > 3600)) return false;
  return v.day <= monthLength(calendar, v.year, v.month);
}

const pad = (n: number, width = 2) => String(n).padStart(width, '0');

export function parseIsoDate(iso: string | null | undefined): YMD | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso ?? '');
  if (!m) return null;
  const v = { year: Number(m[1]), month: Number(m[2]), day: Number(m[3]) };
  return isValidYmd('gregory', v) ? v : null;
}

export function toIsoDate(g: YMD): string {
  return `${pad(g.year, 4)}-${pad(g.month)}-${pad(g.day)}`;
}

/** ISO Gregorian `yyyy-MM-dd` → the same day in the given calendar. */
export function isoToCalendar(iso: string, calendar: DateCalendar): YMD | null {
  const g = parseIsoDate(iso);
  if (!g) return null;
  return calendar === 'persian' ? toJalali(g) : g;
}

/** A day in the given calendar → ISO Gregorian `yyyy-MM-dd` (null when the date does not exist). */
export function calendarToIso(v: YMD, calendar: DateCalendar): string | null {
  if (!isValidYmd(calendar, v)) return null;
  return toIsoDate(calendar === 'persian' ? fromJalali(v) : v);
}

/** Calendar used for date entry: Jalali for fa, Gregorian for every other locale (OD-020). */
export function inputCalendarOf(locale: string): DateCalendar {
  return getMetadata(locale).calendar === 'persian' ? 'persian' : 'gregory';
}

const PERSIAN_DIGITS = '۰۱۲۳۴۵۶۷۸۹';
const ARABIC_DIGITS = '٠١٢٣٤٥٦٧٨٩';

/** Persian/Arabic-Indic digits → ASCII digits. */
export function normalizeDigits(text: string): string {
  return text.replace(/[۰-۹٠-٩]/g, (c) => {
    const p = PERSIAN_DIGITS.indexOf(c);
    return String(p >= 0 ? p : ARABIC_DIGITS.indexOf(c));
  });
}

export function localizeDigits(text: string, locale: string): string {
  const system = getMetadata(locale).numberingSystem;
  const digits = system === 'arabext' ? PERSIAN_DIGITS : system === 'arab' ? ARABIC_DIGITS : null;
  return digits ? text.replace(/\d/g, (d) => digits[Number(d)]!) : text;
}

/**
 * Text shown in a date input: `1405/09/03` (Jalali, Persian digits) for fa, ISO `2026-11-24` otherwise.
 * ASCII digits are kept for non-fa locales so the field round-trips with what users type.
 */
export function formatDateInput(iso: string | null | undefined, locale: string): string {
  if (!iso) return '';
  const calendar = inputCalendarOf(locale);
  const v = isoToCalendar(iso, calendar);
  if (!v) return '';
  if (calendar === 'persian') return localizeDigits(`${pad(v.year, 4)}/${pad(v.month)}/${pad(v.day)}`, locale);
  return toIsoDate(v);
}

/**
 * Parses what a user typed in the locale calendar (`1405/9/3`, `۱۴۰۵-۰۹-۰۳`, `2026-11-24`) into ISO
 * Gregorian. Returns null for anything that is not a real date in that calendar.
 */
export function parseDateInput(text: string, locale: string): string | null {
  const m = /^\s*(\d{3,4})\s*[-/.]\s*(\d{1,2})\s*[-/.]\s*(\d{1,2})\s*$/.exec(normalizeDigits(text));
  if (!m) return null;
  return calendarToIso({ year: Number(m[1]), month: Number(m[2]), day: Number(m[3]) }, inputCalendarOf(locale));
}

/** 0 = Sunday … 6 = Saturday, for an ISO Gregorian date. */
export function isoWeekdayIndex(iso: string): number {
  const g = parseIsoDate(iso);
  if (!g) return -1;
  return mod(g2d(g.year, g.month, g.day) + 1, 7);
}

/** API weekday names, Saturday first (Iranian week, used for every timetable). */
export const WEEKDAYS = ['saturday', 'sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday'] as const;
export type Weekday = (typeof WEEKDAYS)[number];
const SUNDAY_FIRST: Weekday[] = ['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday'];

export function weekdayOfIso(iso: string): Weekday | null {
  const i = isoWeekdayIndex(iso);
  return i < 0 ? null : SUNDAY_FIRST[i]!;
}

/** Sort key for a weekday in a Saturday-first week. */
export const weekdayOrder = (day: string) => {
  const i = (WEEKDAYS as readonly string[]).indexOf(day);
  return i < 0 ? 99 : i;
};

export function addDaysIso(iso: string, days: number): string | null {
  const g = parseIsoDate(iso);
  if (!g) return null;
  return toIsoDate(d2g(g2d(g.year, g.month, g.day) + days));
}

export interface MonthGridDay {
  iso: string;
  day: number;
  inMonth: boolean;
}

/**
 * Weeks of a month in the given calendar, each a row of 7 days starting on Saturday,
 * padded with days of the neighbouring months.
 */
export function monthGrid(calendar: DateCalendar, year: number, month: number): MonthGridDay[][] {
  const firstIso = calendarToIso({ year, month, day: 1 }, calendar);
  if (!firstIso) return [];
  const length = monthLength(calendar, year, month);
  // Saturday-first offset: Saturday (index 6) → 0, Sunday (0) → 1, …
  const offset = (isoWeekdayIndex(firstIso) + 1) % 7;
  const cells = Math.ceil((offset + length) / 7) * 7;
  const weeks: MonthGridDay[][] = [];
  for (let i = 0; i < cells; i += 1) {
    const iso = addDaysIso(firstIso, i - offset)!;
    const v = isoToCalendar(iso, calendar)!;
    if (i % 7 === 0) weeks.push([]);
    weeks[weeks.length - 1]!.push({ iso, day: v.day, inMonth: i >= offset && i < offset + length });
  }
  return weeks;
}

/** Today's calendar date (ISO) in an IANA time zone, e.g. the tenant zone used by the API's "today". */
export function todayIso(timeZone: string, now: Date = new Date()): string {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(now);
  const get = (type: string) => parts.find((p) => p.type === type)?.value ?? '';
  return `${get('year')}-${get('month')}-${get('day')}`;
}
