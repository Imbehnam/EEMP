import { securityHeaders } from '@eemp/config/security-headers';
import type { NextConfig } from 'next';
import createNextIntlPlugin from 'next-intl/plugin';

const withNextIntl = createNextIntlPlugin('./src/i18n/request.ts');

const nextConfig: NextConfig = {
  reactStrictMode: true,
  poweredByHeader: false,
  // Workspace packages ship TypeScript source.
  transpilePackages: ['@eemp/ui', '@eemp/i18n', '@eemp/api-client', '@eemp/app'],
  compiler: { emotion: true },
  async headers() {
    return [{ source: '/:path*', headers: securityHeaders({ production: process.env.NODE_ENV === 'production' }) }];
  },
};

export default withNextIntl(nextConfig);
