import createCache, { type EmotionCache } from '@emotion/cache';
import { prefixer } from 'stylis';
import rtlPlugin from 'stylis-plugin-rtl';

export type Dir = 'rtl' | 'ltr';

/**
 * One cache per direction. For RTL, stylis-plugin-rtl flips any physical properties
 * (margin-left, left, text-align: left, ...) — components still prefer logical properties
 * (margin-inline-start, inset-inline-start), which the plugin leaves untouched.
 */
export function createEmotionCache(dir: Dir, nonce?: string): EmotionCache {
  return createCache({
    key: dir === 'rtl' ? 'eemp-rtl' : 'eemp',
    nonce,
    prepend: true,
    stylisPlugins: dir === 'rtl' ? [prefixer, rtlPlugin] : [prefixer],
  });
}
