'use client';

import type { Branch, CurrentUser } from '@eemp/api-client';
import { useApiQuery, useSession } from '@eemp/app';
import { Link } from '@eemp/i18n/navigation';
import { Badge, Card, PageHeader } from '@eemp/ui';
import { useTranslations } from 'next-intl';

import { adminNavigation } from '../navigation';

export function ScopeList({ user, branches }: { user: CurrentUser; branches: Branch[] }) {
  const shell = useTranslations('shell');
  const portal = useTranslations('portal');
  if (user.scopes.length === 0) return <p>{portal('noRoles')}</p>;
  return (
    <ul style={{ margin: 0, paddingInlineStart: 20 }}>
      {user.scopes.map((s, i) => (
        <li key={`${s.roleCode}-${s.branchId ?? 'all'}-${i}`}>
          {shell('scopeItem', {
            role: s.roleCode,
            branch: s.branchId ? (branches.find((b) => b.id === s.branchId)?.name ?? s.branchId) : shell('tenantWide'),
          })}
        </li>
      ))}
    </ul>
  );
}

export function DashboardScreen() {
  const t = useTranslations('dashboard');
  const nav = useTranslations('nav');
  const { user, can } = useSession();
  const branches = useApiQuery('dashboard-branches', async (api) =>
    can('branches.read') ? (await api.branches.list({ pageSize: 100 })).items : ([] as Branch[]),
  );
  const links = adminNavigation(user).flatMap((s) => s.items).filter((i) => i.href !== '/');
  return (
    <>
      <PageHeader title={t('welcome', { name: user.displayName })} description={user.isPlatformAdmin ? t('platformIntro') : t('tenantIntro')} />
      <div style={{ display: 'grid', gap: 16, gridTemplateColumns: 'repeat(auto-fit, minmax(18rem, 1fr))' }}>
        {!user.isPlatformAdmin && (
          <Card aria-labelledby="access-heading">
            <h2 id="access-heading" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('yourAccess')}</h2>
            <p>
              <Badge tone="info">{t('permissionCount', { count: user.permissions.length })}</Badge>
            </p>
            {user.permissions.length === 0 ? <p>{t('noPermissions')}</p> : <ScopeList user={user} branches={branches.data ?? []} />}
          </Card>
        )}
        <Card aria-labelledby="links-heading">
          <h2 id="links-heading" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('quickLinks')}</h2>
          <ul style={{ margin: 0, paddingInlineStart: 20, display: 'grid', gap: 4 }}>
            {links.map((l) => (
              <li key={l.href}>
                <Link href={l.href}>{nav(l.labelKey)}</Link>
              </li>
            ))}
          </ul>
        </Card>
      </div>
    </>
  );
}
