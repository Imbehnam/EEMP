import type { NextRequest } from 'next/server';

import { authHandlers } from '../handlers';

export const dynamic = 'force-dynamic';

export function GET(request: NextRequest) {
  return authHandlers.refresh(request);
}
