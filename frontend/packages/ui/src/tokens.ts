import '@emotion/react';

/**
 * Design tokens. Colors are CSS custom properties (defined in styles.css for light and dark),
 * so Emotion styles and Tailwind utilities share one source and switch theme without re-render.
 * Contrast pairs meet WCAG 2.2 AA (>= 4.5:1 for text, >= 3:1 for UI boundaries); see test/contrast.test.ts.
 * Visual language: accessible soft UI (neumorphism) — docs/05-experience/DESIGN_SYSTEM.md.
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
  dangerFg: 'var(--eemp-color-danger-fg)',
  dangerSoft: 'var(--eemp-color-danger-soft)',
  success: 'var(--eemp-color-success)',
  successSoft: 'var(--eemp-color-success-soft)',
  warning: 'var(--eemp-color-warning)',
  warningSoft: 'var(--eemp-color-warning-soft)',
  info: 'var(--eemp-color-info)',
  infoSoft: 'var(--eemp-color-info-soft)',
  focus: 'var(--eemp-color-focus)',
  backdrop: 'var(--eemp-color-backdrop)',
} as const;

export const space = { 0: '0', 1: '0.25rem', 2: '0.5rem', 3: '0.75rem', 4: '1rem', 5: '1.25rem', 6: '1.5rem', 8: '2rem', 10: '2.5rem', 12: '3rem' } as const;
export const radii = { sm: 'var(--eemp-radius-sm)', md: 'var(--eemp-radius-md)', lg: 'var(--eemp-radius-lg)', full: '9999px' } as const;
export const motion = { fast: '120ms', base: '180ms', ease: 'var(--eemp-ease)' } as const;
export const fontSizes = { xs: '0.75rem', sm: '0.875rem', md: '1rem', lg: '1.125rem', xl: '1.375rem', '2xl': '1.75rem', '3xl': '2.125rem' } as const;
export const fontWeights = { regular: 400, medium: 500, semibold: 600, bold: 700 } as const;
/** Neumorphic elevation: raised = dual light/dark drop shadows, inset/pressed = inner shadows. Light source mirrors in RTL. */
export const shadows = {
  raisedSm: 'var(--eemp-shadow-raised-sm)',
  raised: 'var(--eemp-shadow-raised)',
  raisedLg: 'var(--eemp-shadow-raised-lg)',
  inset: 'var(--eemp-shadow-inset)',
  pressed: 'var(--eemp-shadow-pressed)',
  card: 'var(--eemp-shadow-raised)',
  overlay: 'var(--eemp-shadow-raised-lg)',
} as const;
export const breakpoints = { sm: '640px', md: '768px', lg: '1024px', xl: '1280px' } as const;
export const zIndices = { header: 20, drawer: 30, dialog: 40, toast: 50 } as const;
/**
 * Interactive target heights. md/lg are >= 44px everywhere; sm (36px) is only for dense desktop
 * tables and grows to 44px on coarse pointers (touch) — see `touchTarget`.
 */
export const controlHeight = { sm: '2.25rem', md: '2.75rem', lg: '3.25rem' } as const;
export const touchTarget = '2.75rem';
export const coarsePointer = '@media (pointer: coarse)';

export const tokens = { colors, space, radii, motion, fontSizes, fontWeights, shadows, breakpoints, zIndices, controlHeight } as const;
export type Tokens = typeof tokens;

export const mq = (bp: keyof typeof breakpoints) => `@media (min-width: ${breakpoints[bp]})`;

// Typed Emotion theme (declared here so every package compiling these sources picks it up).
declare module '@emotion/react' {
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type
  export interface Theme extends Tokens {}
}
