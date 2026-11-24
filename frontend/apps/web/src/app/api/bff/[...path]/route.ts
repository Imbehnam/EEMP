import { createBffProxy } from '@eemp/api-client/server';
import type { NextRequest } from 'next/server';

import { APP_ID } from '../../auth/handlers';

export const dynamic = 'force-dynamic';

const proxy = createBffProxy({ appId: APP_ID });
type Context = { params: Promise<{ path: string[] }> };

export function GET(request: NextRequest, context: Context) {
  return proxy.GET(request, context);
}
export function POST(request: NextRequest, context: Context) {
  return proxy.POST(request, context);
}
export function PUT(request: NextRequest, context: Context) {
  return proxy.PUT(request, context);
}
export function DELETE(request: NextRequest, context: Context) {
  return proxy.DELETE(request, context);
}
export function PATCH(request: NextRequest, context: Context) {
  return proxy.PATCH(request, context);
}
