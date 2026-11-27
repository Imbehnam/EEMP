'use client';

import { createContext, useContext, type ReactNode, type SVGProps } from 'react';

import type { Dir } from '../createEmotionCache';

/**
 * The document direction for components that draw direction-specific artwork (DL-086). EmotionRegistry provides it from the
 * locale, so the server render and the client agree and nothing depends on a stylesheet having loaded.
 */
const DirectionContext = createContext<Dir | null>(null);

export function DirectionProvider({ dir, children }: { dir: Dir; children: ReactNode }) {
  return <DirectionContext.Provider value={dir}>{children}</DirectionContext.Provider>;
}

/** The current direction, or null outside a provider (then components fall back to CSS selection). */
export function useDirection(): Dir | null {
  return useContext(DirectionContext);
}

const RIGHT = 'm9 6 6 6-6 6';
const LEFT = 'm15 6-6 6 6 6';

/** Which way "towards" points on screen: end = forward (right in LTR, left in RTL); start = back. */
export function physicalSide(towards: 'end' | 'start', dir: Dir): 'left' | 'right' {
  return (towards === 'end') === (dir === 'ltr') ? 'right' : 'left';
}

type ChevronProps = Omit<SVGProps<SVGSVGElement>, 'children'> & { size?: number; towards?: 'end' | 'start' };

/**
 * Chevron pointing to the inline end (forward: "view all", "next", breadcrumb separators) or, with towards="start", the inline
 * start ("previous", "back"). With a DirectionProvider (always, inside the apps) exactly one path is drawn for the known direction;
 * without one, both are drawn and styles.css hides the one that does not match the document direction.
 */
export function IconChevron({ towards = 'end', size = 20, ...rest }: ChevronProps) {
  const dir = useDirection();
  const side = dir ? physicalSide(towards, dir) : null;
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      data-towards={towards}
      data-side={side ?? undefined}
      {...rest}
    >
      {side ? (
        <path d={side === 'right' ? RIGHT : LEFT} />
      ) : (
        <>
          <path className="eemp-ltr-only" d={towards === 'end' ? RIGHT : LEFT} />
          <path className="eemp-rtl-only" d={towards === 'end' ? LEFT : RIGHT} />
        </>
      )}
    </svg>
  );
}

const GLYPHS = { chevron: { left: '‹', right: '›' }, arrow: { left: '←', right: '→' } } as const;

/** A decorative text arrow/chevron (‹ › ← →) pointing to the inline start or end for the current direction. */
export function DirectionalGlyph({ towards, kind = 'arrow' }: { towards: 'end' | 'start'; kind?: 'arrow' | 'chevron' }) {
  const dir = useDirection();
  if (!dir) {
    // No provider: keep the CSS-mirrored glyph (styles.css .eemp-flip).
    return <span aria-hidden="true" className="eemp-flip">{GLYPHS[kind][towards === 'end' ? 'right' : 'left']}</span>;
  }
  return <span aria-hidden="true" data-side={physicalSide(towards, dir)}>{GLYPHS[kind][physicalSide(towards, dir)]}</span>;
}
