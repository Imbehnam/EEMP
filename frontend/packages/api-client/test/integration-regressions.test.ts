import { describe, expect, it } from 'vitest';

import { ApiError, groupPermissionsByResource, type Permission } from '../src';
import { forwardedFor } from '../src/server/handlers';
import { apiLocales, toApiLocale, toSupportedLocale } from '../../i18n/src/locales';

/** Regressions found running the frontend against the real .NET API (M1 integration, 2026-11-24). */
describe('M1 real-API integration regressions', () => {
  it('only concurrency.conflict is a concurrency conflict; other 409s keep their own code and map to the field', () => {
    const conflict = new ApiError(409, { code: 'concurrency.conflict' });
    expect(conflict.isConflict).toBe(true);
    const duplicate = new ApiError(409, { code: 'branch.code_taken', params: { field: 'code' } });
    expect(duplicate.isConflict).toBe(false);
    expect(duplicate.fieldErrors).toEqual({ code: ['branch.code_taken'] });
    const inUse = new ApiError(409, { code: 'role.in_use' });
    expect(inUse.isConflict).toBe(false);
    expect(inUse.fieldErrors).toEqual({});
  });

  it('permission matrix groups by resource, not by the API module group', () => {
    const p = (code: string, module: string): Permission => ({ code, module, isSensitive: false, branchScoped: true });
    const groups = groupPermissionsByResource([
      p('students.read', 'people'), p('students.manage', 'people'),
      p('guardians.read', 'people'), p('guardians.manage', 'people'),
      p('branches.read', 'organization'),
    ]);
    expect(groups.map((g) => g.module)).toEqual(['students', 'guardians', 'branches']);
    expect(groups[1]!.actions.map((a) => a.code)).toEqual(['guardians.read', 'guardians.manage']);
  });

  it('UI locale fa is sent to the API as fa-IR and read back as fa', () => {
    expect(apiLocales).toEqual({ fa: 'fa-IR', en: 'en', tr: 'tr', ar: 'ar' });
    expect(toApiLocale('fa')).toBe('fa-IR');
    expect(toApiLocale('fa-IR')).toBe('fa-IR');
    expect(toApiLocale('tr')).toBe('tr');
    expect(toSupportedLocale('fa-IR')).toBe('fa');
  });

  it('BFF forwards the browser address to the API (per-client auth rate limit)', () => {
    expect(forwardedFor({ headers: new Headers({ 'x-forwarded-for': '203.0.113.7' }) })).toEqual({ 'X-Forwarded-For': '203.0.113.7' });
    expect(forwardedFor({ headers: new Headers() })).toEqual({});
  });
});
