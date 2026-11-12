# Internationalization and Localization (i18n)

## Requirements
- Locale can be selected per user; tenant has a default locale and supported locale list.
- UI uses translation keys; no user-facing hard-coded strings.
- Support RTL and LTR at layout, component, icon, form, table, and chart levels.
- Format dates, numbers, percentages, currencies, and plural forms using locale-aware libraries.
- Persist instants in UTC; display in user/tenant timezone.
- Store timezone as IANA identifier. Do not assume server timezone.
- Keep language, locale, timezone, calendar system, currency, and numbering system as distinct settings.
- Support translated content with fallback locale and missing-translation reporting.
- Localize SMS/email templates, invoice/receipt, report card, digital card, and generated PDFs.
- Use Unicode-safe search and collation strategy; validate Persian/Arabic text, Latin names, and mixed-direction identifiers.
- Avoid concatenating translated fragments; use parameterized ICU-style messages.
- Translate validation errors through stable error codes.
- Accessibility: language switcher, correct `lang` and `dir`, keyboard support, screen-reader labels.

## Frontend
Use next-intl or an equivalent maintained solution. Define locale routing and dictionary namespaces by module. Set `dir=rtl|ltr` from locale metadata. Ensure server and client render the same locale to avoid hydration mismatch.

## Backend
Return stable error codes and structured parameters; do not return English-only messages as the only client contract. Use locale-aware templates for generated documents and outbound messages.

## Data model
Keep canonical business values language-neutral. For translatable catalog data, use normalized translation records or JSON only after query/report requirements are understood. Do not translate user-entered personal names automatically.

## Initial locale candidates
Persian (fa-IR, RTL), English (en, LTR), Arabic (ar, RTL), Turkish (tr, LTR). Final supported locales are a product decision.
