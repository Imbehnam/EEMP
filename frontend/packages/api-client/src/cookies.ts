/** Cookie names are namespaced per app so admin and client portals can share a host in development. */
export interface CookieNames {
  access: string;
  refresh: string;
  csrf: string;
}

export function cookieNames(appId: string): CookieNames {
  return { access: `eemp_${appId}_at`, refresh: `eemp_${appId}_rt`, csrf: `eemp_${appId}_csrf` };
}
