import { createMockBackend, type MockBackend } from './backend';

export { createMockBackend, DEFAULT_MOCK_PASSWORD, MOCK_API_BASE_URL } from './backend';
export type { MockBackend, MockBackendOptions } from './backend';
export { IDS as MOCK_IDS } from './seed';

const KEY = Symbol.for('eemp.mockBackend');

/** Process-wide mock instance so state survives across Next.js route handler invocations. */
export function getSharedMockBackend(): MockBackend {
  const g = globalThis as unknown as Record<symbol, MockBackend | undefined>;
  if (!g[KEY]) {
    g[KEY] = createMockBackend({ password: process.env.EEMP_MOCK_PASSWORD || undefined });
  }
  return g[KEY]!;
}
