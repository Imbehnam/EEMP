'use client';

import { PortalHomeScreen } from '../../../screens/portal';
import { usePortalKind } from './portal-kind';

export function Home() {
  return <PortalHomeScreen kind={usePortalKind()} />;
}
