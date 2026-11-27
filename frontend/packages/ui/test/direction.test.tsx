import { CacheProvider, ThemeProvider } from '@emotion/react';
import type { ReactNode } from 'react';
import { renderToString } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import { createEmotionCache, DirectionalGlyph, DirectionProvider, IconChevron, Pagination, physicalSide, tokens } from '../src';

/** DL-086: forward chevrons point to the inline end — right in LTR, left in RTL — without depending on a stylesheet. */
const RIGHT = 'm9 6 6 6-6 6';
const LEFT = 'm15 6-6 6 6 6';

function render(dir: 'ltr' | 'rtl', node: ReactNode) {
  return renderToString(
    <CacheProvider value={createEmotionCache(dir)}>
      <ThemeProvider theme={tokens}>
        <DirectionProvider dir={dir}>{node}</DirectionProvider>
      </ThemeProvider>
    </CacheProvider>,
  );
}

const paths = (html: string) => [...html.matchAll(/<path d="([^"]+)"/g)].map((m) => m[1]);

describe('direction-aware chevrons', () => {
  it('maps start/end to the physical side for each direction', () => {
    expect(physicalSide('end', 'ltr')).toBe('right');
    expect(physicalSide('start', 'ltr')).toBe('left');
    expect(physicalSide('end', 'rtl')).toBe('left');
    expect(physicalSide('start', 'rtl')).toBe('right');
  });

  it.each([
    ['ltr', 'end', RIGHT],
    ['ltr', 'start', LEFT],
    ['rtl', 'end', LEFT],
    ['rtl', 'start', RIGHT],
  ] as const)('%s towards=%s draws exactly one path', (dir, towards, d) => {
    expect(paths(render(dir, <IconChevron towards={towards} />))).toEqual([d]);
  });

  it.each([
    ['ltr', [LEFT, RIGHT]],
    ['rtl', [RIGHT, LEFT]],
  ] as const)('pagination in %s: previous points to the start, next to the end', (dir, expected) => {
    const html = render(dir, (
      <Pagination page={2} pageSize={10} totalCount={50} onPageChange={() => {}} labels={{ nav: 'n', summary: 's', previous: 'prev', next: 'next' }} />
    ));
    expect(paths(html)).toEqual(expected);
  });

  it('text glyphs follow the direction', () => {
    expect(render('rtl', <DirectionalGlyph towards="start" />)).toContain('→');
    expect(render('ltr', <DirectionalGlyph towards="start" />)).toContain('←');
    expect(render('rtl', <DirectionalGlyph towards="end" kind="chevron" />)).toContain('‹');
    expect(render('ltr', <DirectionalGlyph towards="end" kind="chevron" />)).toContain('›');
  });

  it('without a provider both paths are drawn for the CSS fallback', () => {
    const html = renderToString(<IconChevron />);
    expect(html).toContain('eemp-ltr-only');
    expect(html).toContain('eemp-rtl-only');
  });
});
