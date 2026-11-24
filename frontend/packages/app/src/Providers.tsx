'use client';

import { EmotionRegistry, type Dir } from '@eemp/ui';
import type { ReactNode } from 'react';

/** Client providers shared by every page (Emotion SSR registry with RTL-aware cache + theme). */
export function AppProviders({ dir, nonce, children }: { dir: Dir; nonce?: string; children: ReactNode }) {
  return (
    <EmotionRegistry key={dir} dir={dir} nonce={nonce}>
      {children}
    </EmotionRegistry>
  );
}
