'use client';

import { CacheProvider, ThemeProvider } from '@emotion/react';
import { useServerInsertedHTML } from 'next/navigation';
import { useState, type ReactNode } from 'react';

import { createEmotionCache, type Dir } from './createEmotionCache';
import { tokens } from './tokens';

export interface EmotionRegistryProps {
  dir: Dir;
  /** CSP nonce from the proxy; applied to SSR and runtime <style> tags. */
  nonce?: string;
  children: ReactNode;
}

/**
 * Emotion + Next.js App Router SSR: collects styles inserted while rendering on the server and
 * flushes them into the HTML stream with useServerInsertedHTML, so pages render styled with no
 * flash and hydrate without mismatches. Remount (key={dir}) when the direction changes.
 */
export function EmotionRegistry({ dir, nonce, children }: EmotionRegistryProps) {
  const [registry] = useState(() => {
    const cache = createEmotionCache(dir, nonce);
    cache.compat = true;
    const originalInsert = cache.insert;
    let inserted: { name: string; isGlobal: boolean }[] = [];
    cache.insert = (...args) => {
      const [selector, serialized] = args;
      if (cache.inserted[serialized.name] === undefined) {
        inserted.push({ name: serialized.name, isGlobal: !selector });
      }
      return originalInsert(...args);
    };
    const flush = () => {
      const previous = inserted;
      inserted = [];
      return previous;
    };
    return { cache, flush };
  });

  useServerInsertedHTML(() => {
    const names = registry.flush();
    if (names.length === 0) return null;
    let styles = '';
    let dataEmotionAttribute = registry.cache.key;
    const globals: { name: string; style: string }[] = [];
    for (const { name, isGlobal } of names) {
      const style = registry.cache.inserted[name];
      if (typeof style !== 'string') continue;
      if (isGlobal) globals.push({ name, style });
      else {
        styles += style;
        dataEmotionAttribute += ` ${name}`;
      }
    }
    return (
      <>
        {globals.map(({ name, style }) => (
          <style
            key={name}
            nonce={nonce}
            data-emotion={`${registry.cache.key}-global ${name}`}
            dangerouslySetInnerHTML={{ __html: style }}
          />
        ))}
        {styles && (
          <style nonce={nonce} data-emotion={dataEmotionAttribute} dangerouslySetInnerHTML={{ __html: styles }} />
        )}
      </>
    );
  });

  return (
    <CacheProvider value={registry.cache}>
      <ThemeProvider theme={tokens}>{children}</ThemeProvider>
    </CacheProvider>
  );
}
