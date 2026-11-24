import type { NextRequest } from 'next/server';

import { authHandlers } from '../handlers';

export const dynamic = 'force-dynamic';

export function POST(request: NextRequest) {
  return authHandlers.logout(request);
}
