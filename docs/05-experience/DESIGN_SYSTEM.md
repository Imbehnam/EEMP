# EEMP design system — accessible soft UI

Status: implemented on `feature/ui-neumorphism` (DL-080). Applies to both Next.js apps (admin console, client portal) through
`frontend/packages/ui` (tokens + Emotion components + `styles.css`) and the Tailwind v4 preset in `frontend/packages/config`.

The visual language is **neumorphism** (soft, extruded surfaces on one base colour) with the known accessibility problems of that
style designed out. The rule of thumb: *depth decorates, it never carries meaning alone.*

## 1. Principles

1. **One soft base per theme.** Page, header, sidebar and cards share a near-identical tone; depth comes from a pair of shadows
   (light from the top-start corner, dark towards the bottom-end).
2. **Raised = can be read or pressed; inset = holds a value or is selected.** Cards, tables, nav hover, secondary buttons and tiles are
   raised. Inputs, selects, checkboxes/radios, the selected nav item, pressed buttons and empty-state icons are inset.
3. **Never shadow-only.** Every boundary that matters has a second cue:
   - text fields/selects/checkboxes: a 1px `border-strong` hairline (≥ 3:1 against every surface);
   - primary and destructive actions: solid accent fill (`primary`, `danger`), not a raised grey button;
   - selected navigation: inset + accent bar on the inline-start edge + bold accent text;
   - tables: hairline row separators + subtle zebra + a stronger header rule;
   - status: tone colour **+ icon + text** (alerts, field errors, toasts, dialog), chips carry a dot and the status word.
4. **Focus is always visible:** 3px solid `--eemp-color-focus` outline with 2px offset on every interactive element
   (`:focus-visible`), independent of shadows.
5. **Disabled is distinguishable without colour:** flat (no shadow), dashed border, muted text, `not-allowed` cursor. Busy buttons keep
   their colour, show a spinner and `aria-busy`.
6. **RTL is first-class.** Logical properties everywhere; the light source mirrors with the document direction via `--eemp-lx`
   (`1` in LTR, `-1` in RTL) so shadows fall towards the reading end; chevrons are drawn per direction (`IconChevron`).
7. **Motion is subtle and optional:** 120–200 ms press/hover transitions, small entrance animations for drawer, dialog and toasts; all
   disabled by `prefers-reduced-motion: reduce`.

## 2. Tokens (CSS custom properties, `packages/ui/src/styles.css`)

| Token | Light | Dark | Use |
|---|---|---|---|
| `--eemp-color-bg` | `#e6ebf1` | `#1e252f` | page, header, sidebar |
| `--eemp-color-surface` | `#eaeff4` | `#222a35` | raised cards, tables, dialogs |
| `--eemp-color-surface-muted` | `#dde3ea` | `#19202a` | inset wells: inputs, table header, selected nav |
| `--eemp-color-border` | `#c3ccd7` | `#343e4c` | decorative hairlines, row separators |
| `--eemp-color-border-strong` | `#6a7586` | `#7a879a` | control boundaries (≥ 3:1) |
| `--eemp-color-fg` / `-fg-muted` | `#1a2433` / `#475467` | `#e7ecf3` / `#a8b3c2` | text (≥ 4.5:1 on every surface) |
| `--eemp-color-primary` / `-primary-fg` | `#0a58a8` / `#fff` | `#79b3f2` / `#0b1726` | accent fills, links |
| `--eemp-color-danger` / `-danger-fg` | `#b3261e` / `#fff` | `#ff9b91` / `#1f0b0a` | destructive actions, errors |
| `--eemp-color-success`, `-warning`, `-info` (+ `-soft`) | AA on bg and on their `-soft` tint | same | status |
| `--eemp-color-focus` | `#0a58a8` | `#9cc8f7` | focus ring |
| `--eemp-shadow-raised-sm/raised/raised-lg` | dual shadow 3/6/12px | same recipe, darker pair | elevation |
| `--eemp-shadow-inset`, `--eemp-shadow-pressed` | inner pair 2/3px | same | wells, pressed/selected |
| `--eemp-radius-sm/md/lg` | 10 / 12 / 20px | | chips / controls / cards, tables, dialogs |

TypeScript mirrors (`tokens.ts`): `colors`, `shadows` (`raisedSm`, `raised`, `raisedLg`, `inset`, `pressed`), `radii`, `motion`,
`space`, `fontSizes` (xs…3xl), `controlHeight` (`sm` 36px, `md` 44px, `lg` 52px), `breakpoints`, `zIndices`.
Tailwind utilities (`bg-surface`, `text-muted`, `rounded-lg`, `shadow-card`, …) map to the same variables via the preset.

Theme: `eemp_theme` cookie (`light` | `dark` | `system`) → `<html data-theme>`; `system` follows `prefers-color-scheme`.

**Contrast is tested**, not assumed: `packages/ui/test/contrast.test.ts` parses `styles.css` and checks, for both themes, text colours on
`bg`/`surface`/`surface-muted` (≥ 4.5:1), `border-strong` and `focus` (≥ 3:1), accent fills vs. their foreground, and each tone on its
soft tint. Change a colour → the test tells you whether it still passes.

## 3. Typography

- **Vazirmatn (variable)** for every locale: it covers Arabic script, Latin and Latin-Extended (Turkish), so mixed Persian/English
  content (names, codes, emails) stays in one family; system UI stack as fallback.
- Base 16px; line-height 1.6 (fa/ar) and 1.55 (en/tr); headings 1.35. Page title 22px on phones, 28px from `md`.
- Numbers: `font-variant-numeric: tabular-nums` on tables, KPIs and money; digits come from the locale formatter (Persian digits in fa).
  Numeric columns use `align: 'end'`.
- Latin identifiers inside RTL text keep `class="eemp-ltr"` (direction isolate).

## 4. Components (all in `@eemp/ui`, picked up by every screen)

| Component | Rules |
|---|---|
| `Button` / `ButtonLink` | primary & danger = solid fill + small raised shadow; secondary = raised soft + `border-strong`; ghost = text. `:active` → pressed inset. Heights 44px (md), 52px (lg); `sm` 36px grows to 44px on coarse pointers. |
| `TextField`, `SelectField`, `TextAreaField`, raw `input/select/textarea` | inset well + `border-strong` hairline; hover darkens the border; invalid = danger border + ring + icon + message linked by `aria-describedby`; read-only/disabled = flat, dashed. |
| Checkbox / radio (global) | 22px inset well, accent fill + check/dot when on, indeterminate bar, dashed hatch when disabled; row target ≥ 44px. |
| `Card` | raised, radius 20, faint edge (`--eemp-card-edge`) so it stays legible in dark mode and print. |
| `DataTable` | raised card; sticky header inside a height-capped scroller; hairline rows, zebra, hover tint; tabular numbers; skeleton rows while loading (with a visually hidden status). |
| `PagedList` (admin) | search (pill, leading icon) + table + pagination; empty state via `StatePanel`. |
| `Pagination` | secondary buttons with direction-aware chevrons, live summary. |
| `StatePanel` | inset icon (inbox / tone), title, description, optional next-step action. |
| `Alert` | tone tint + inline-start bar + icon + text; danger/warning `role=alert`, others `role=status`. |
| `Badge` | tinted pill, dot + word. |
| `ConfirmDialog` | native `<dialog>` (focus trap, Escape, focus return), raised-lg, warning icon for destructive tone, cancel focused first. |
| `ToastProvider` / `useToast` | bottom inline-end stack, max 3, raised-lg, icon + text + close button, 7s auto-dismiss paused on hover/focus; `role=status` (`alert` for danger). Redirect notices (`?notice=created|saved|deleted`) use it. |
| `StatCard` / `StatGrid` | dashboard KPI: label, large tabular value (locale digits), skeleton while loading, "View all" link. |
| `AppShell` | sticky header (menu button < lg, brand mark, brand + tenant); persistent sidebar ≥ 1024px, off-canvas drawer with backdrop below (Escape/backdrop/close button, focus moved in and returned); language/theme/sign-out move into the drawer below 768px; breadcrumbs (Home › Section › Page) generated from the navigation; content max width 1440px. |
| `PageHeader` | breadcrumb (from shell) → optional back link → title + description; actions at the inline end, primary last. |
| Login | brand panel (inset) + raised form card on ≥ 1024px, single card on phones; large full-width primary button. |
| Tiles (`.eemp-tile`) | raised link cards with an inset chevron badge, press-in on `:active` (portal home, dashboard quick links). |

## 5. Layout and UX rules

- Every screen: breadcrumb → `PageHeader` (title, one-line description, actions) → content. Primary action sits at the inline end of the
  header; destructive actions always go through `ConfirmDialog`.
- Lists: search first, table second, pagination last; empty states explain what is missing and offer the next step where the user can
  act.
- Forms: `FormGrid` (auto-fit columns, 16rem min), labels above fields, required mark + `required`, inline errors per field from the
  API's field map, one primary submit + secondary cancel.
- Feedback: inline `Alert` for page-level state, toast for "done" confirmations after navigation, skeletons for first loads.
- Touch: interactive targets ≥ 44px (WCAG 2.5.5 AAA level met for main controls; 2.5.8 AA everywhere).
- Text never relies on colour alone; icons are decorative (`aria-hidden`) and paired with text.

## 6. Quality gates

- `pnpm test`: `contrast.test.ts` (palette), `ssr.test.tsx` (every component renders through the LTR and RTL Emotion caches),
  component behaviour tests.
- `pnpm test:e2e`: `e2e/a11y.spec.ts` runs axe-core (WCAG 2.0/2.1/2.2 A+AA tags) on login, dashboard, a list, a form (admin) and the
  portal home, in fa and en, light and dark; any serious or critical violation fails the build.
- Design review screenshots: `pnpm exec playwright test -c playwright.shots.config.ts` with `SHOTS_DIR`, `SHOTS_PREFIX` and optional
  `SHOTS_THEME=dark` (desktop 1440 and mobile 390, fa).

## 7. Gotchas

- Emotion bundles its own stylis; `createEmotionCache` adds the stylis 4.3 `prefixer`. Rules the prefixer *copies* (e.g.
  `::placeholder`, `:read-only`) crash SSR in that combination, so keep those selectors in `styles.css`, not in Emotion styles
  (`ssr.test.tsx` guards this).
- Do not mirror icons with CSS transforms on `<svg>` (unreliably painted in Chromium); draw direction-specific paths
  (`.eemp-ltr-only` / `.eemp-rtl-only`) as `IconChevron` does. Text glyphs like `‹ ›` are already mirrored by the bidi algorithm.
