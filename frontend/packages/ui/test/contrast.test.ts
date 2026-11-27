import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

/**
 * Guards the soft-UI palette: neumorphism tends to drift into low contrast, so every text and
 * control-boundary pair is checked against WCAG 2.2 AA in both themes.
 */
const css = readFileSync(fileURLToPath(new URL('../src/styles.css', import.meta.url)), 'utf8');

function block(selector: string): Record<string, string> {
  const start = css.indexOf(`${selector} {`);
  if (start < 0) throw new Error(`missing ${selector}`);
  const body = css.slice(start, css.indexOf('}', start));
  const vars: Record<string, string> = {};
  for (const m of body.matchAll(/--eemp-color-([a-z-]+):\s*(#[0-9a-f]{6})/gi)) vars[m[1]!] = m[2]!;
  return vars;
}

function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r! + 0.7152 * g! + 0.0722 * b!;
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi! + 0.05) / (lo! + 0.05);
}

const light = block(':root');
const dark = { ...light, ...block(":root[data-theme='dark']") };
const surfaces = ['bg', 'surface', 'surface-muted'];

describe.each([
  ['light', light],
  ['dark', dark],
])('%s theme contrast', (_name, t) => {
  it.each(surfaces)('text colours on %s meet 4.5:1', (surface) => {
    for (const fg of ['fg', 'fg-muted', 'primary', 'danger', 'success', 'warning', 'info']) {
      expect(contrast(t[fg]!, t[surface]!), `${fg} on ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it.each(surfaces)('control boundaries and focus ring on %s meet 3:1', (surface) => {
    expect(contrast(t['border-strong']!, t[surface]!)).toBeGreaterThanOrEqual(3);
    expect(contrast(t.focus!, t[surface]!)).toBeGreaterThanOrEqual(3);
  });

  it('accent fills keep their label readable', () => {
    expect(contrast(t['primary-fg']!, t.primary!)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(t['danger-fg']!, t.danger!)).toBeGreaterThanOrEqual(4.5);
  });

  it('status chips (tone on tone-soft) and body text on soft fills meet 4.5:1', () => {
    for (const tone of ['danger', 'success', 'warning', 'info', 'primary']) {
      expect(contrast(t[tone]!, t[`${tone}-soft`]!), `${tone} on ${tone}-soft`).toBeGreaterThanOrEqual(4.5);
      expect(contrast(t.fg!, t[`${tone}-soft`]!)).toBeGreaterThanOrEqual(4.5);
    }
  });
});
