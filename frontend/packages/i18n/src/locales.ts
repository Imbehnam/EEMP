/**
 * Locale metadata (OD-019 / OD-020, accepted working assumptions):
 * fa (default, RTL, Jalali), en (LTR), tr (LTR), ar (RTL, Gregorian for now; Hijri later).
 * Language, calendar, numbering system and time zone are kept as distinct settings.
 */
export const locales = ['fa', 'en', 'tr', 'ar'] as const;
export type Locale = (typeof locales)[number];
export const defaultLocale: Locale = 'fa';
export const defaultTimeZone = 'Asia/Tehran';

export type Direction = 'rtl' | 'ltr';
export type CalendarSystem = 'persian' | 'gregory' | 'islamic-umalqura';

export interface LocaleMetadata {
  code: Locale;
  /** BCP-47 tag used for Intl formatting (calendar and numbering stated explicitly). */
  intlLocale: string;
  dir: Direction;
  calendar: CalendarSystem;
  numberingSystem: 'arabext' | 'arab' | 'latn';
  /** Name of the language in itself (not translated; used by the language switcher). */
  nativeName: string;
  /** Font stack family key. Vazirmatn covers Persian and Arabic script. */
  font: 'vazirmatn' | 'system';
}

export const localeMetadata: Record<Locale, LocaleMetadata> = {
  fa: { code: 'fa', intlLocale: 'fa-IR-u-ca-persian-nu-arabext', dir: 'rtl', calendar: 'persian', numberingSystem: 'arabext', nativeName: 'فارسی', font: 'vazirmatn' },
  en: { code: 'en', intlLocale: 'en-US-u-ca-gregory-nu-latn', dir: 'ltr', calendar: 'gregory', numberingSystem: 'latn', nativeName: 'English', font: 'system' },
  tr: { code: 'tr', intlLocale: 'tr-TR-u-ca-gregory-nu-latn', dir: 'ltr', calendar: 'gregory', numberingSystem: 'latn', nativeName: 'Türkçe', font: 'system' },
  ar: { code: 'ar', intlLocale: 'ar-u-ca-gregory-nu-arab', dir: 'rtl', calendar: 'gregory', numberingSystem: 'arab', nativeName: 'العربية', font: 'vazirmatn' },
};

export function isLocale(value: unknown): value is Locale {
  return typeof value === 'string' && (locales as readonly string[]).includes(value);
}

/** Accepts backend values such as "fa-IR" or "en-US" and maps them to a supported route locale. */
export function toSupportedLocale(value: string | null | undefined, fallback: Locale = defaultLocale): Locale {
  if (!value) return fallback;
  if (isLocale(value)) return value;
  const base = value.toLowerCase().split(/[-_]/)[0];
  return isLocale(base) ? base : fallback;
}

export function getDirection(locale: string): Direction {
  return isLocale(locale) ? localeMetadata[locale].dir : 'ltr';
}

export function getMetadata(locale: string): LocaleMetadata {
  return localeMetadata[toSupportedLocale(locale)];
}

/** All IANA zones known to the runtime (falls back to a short list on older engines). */
export function listTimeZones(): string[] {
  const intl = Intl as unknown as { supportedValuesOf?: (key: string) => string[] };
  const zones = intl.supportedValuesOf?.('timeZone') ?? [];
  const base = zones.length ? zones : ['Asia/Tehran', 'Asia/Dubai', 'Europe/Istanbul', 'Asia/Baghdad', 'Europe/London'];
  return base.includes('UTC') ? base : ['UTC', ...base];
}

export function isValidTimeZone(zone: string): boolean {
  try {
    new Intl.DateTimeFormat('en', { timeZone: zone });
    return true;
  } catch {
    return false;
  }
}
