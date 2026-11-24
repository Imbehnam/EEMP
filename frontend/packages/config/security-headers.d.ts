export interface HeaderEntry {
  key: string;
  value: string;
}
export function securityHeaders(opts: { production: boolean }): HeaderEntry[];
