/**
 * Design tokens. Colors are CSS custom properties (defined in styles.css for light and dark),
 * so Emotion styles and Tailwind utilities share one source and switch theme without re-render.
 * Contrast pairs were chosen for WCAG 2.2 AA (>= 4.5:1 for text, >= 3:1 for UI boundaries).
 */
export const colors = {
  bg: 'var(--eemp-color-bg)',
  surface: 'var(--eemp-color-surface)',
  surfaceMuted: 'var(--eemp-color-surface-muted)',
  border: 'var(--eemp-color-border)',
  borderStrong: 'var(--eemp-color-border-strong)',
  fg: 'var(--eemp-color-fg)',
  fgMuted: 'var(--eemp-color-fg-muted)',
  primary: 'var(--eemp-color-primary)',
  primaryHover: 'var(--eemp-color-primary-hover)',
  primaryFg: 'var(--eemp-color-primary-fg)',
  primarySoft: 'var(--eemp-color-primary-soft)',
  danger: 'var(--eemp-color-danger)',
  dangerSoft: 'var(--eemp-color-danger-soft)',
  success: 'var(--eemp-color-success)',
  successSoft: 'var(--eemp-color-success-soft)',
  warning: 'var(--eemp-color-warning)',
  warningSoft: 'var(--eemp-color-warning-soft)',
  info: 'var(--eemp-color-info)',
  infoSoft: 'var(--eemp-color-info-soft)',
  focus: 'var(--eemp-color-focus)',
} as const;

export const space = { 0: '0', 1: '0.25rem', 2: '0.5rem', 3: '0.75rem', 4: '1rem', 5: '1.25rem', 6: '1.5rem', 8: '2rem', 10: '2.5rem', 12: '3rem' } as const;
export const radii = { sm: 'var(--eemp-radius-sm)', md: 'var(--eemp-radius-md)', lg: 'var(--eemp-radius-lg)', full: '9999px' } as const;
export const fontSizes = { xs: '0.75rem', sm: '0.875rem', md: '1rem', lg: '1.125rem', xl: '1.375rem', '2xl': '1.75rem' } as const;
export const fontWeights = { regular: 400, medium: 500, semibold: 600, bold: 700 } as const;
export const shadows = { card: 'var(--eemp-shadow-card)', overlay: 'var(--eemp-shadow-overlay)' } as const;
export const breakpoints = { sm: '640px', md: '768px', lg: '1024px', xl: '1280px' } as const;
export const zIndices = { header: 20, drawer: 30, dialog: 40, toast: 50 } as const;
/** Minimum interactive target (WCAG 2.2 SC 2.5.8 recommends >= 24px; we use 40px). */
export const controlHeight = { sm: '2rem', md: '2.5rem', lg: '3rem' } as const;

export const tokens = { colors, space, radii, fontSizes, fontWeights, shadows, breakpoints, zIndices, controlHeight } as const;
export type Tokens = typeof tokens;

export const mq = (bp: keyof typeof breakpoints) => `@media (min-width: ${breakpoints[bp]})`;
