'use client';

import type { PortalKind } from '@eemp/api-client';
import { createContext, useContext, type ReactNode } from 'react';

const PortalKindContext = createContext<PortalKind | null>(null);

export function PortalKindProvider({ kind, children }: { kind: PortalKind | null; children: ReactNode }) {
  return <PortalKindContext.Provider value={kind}>{children}</PortalKindContext.Provider>;
}

export const usePortalKind = () => useContext(PortalKindContext);
