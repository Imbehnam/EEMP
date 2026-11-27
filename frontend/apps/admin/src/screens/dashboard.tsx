'use client';

import type { Branch, CurrentUser, Endpoints } from '@eemp/api-client';
import { useApiQuery, useSession } from '@eemp/app';
import { Link } from '@eemp/i18n/navigation';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Badge, Card, IconChevron, PageHeader, StatCard, StatGrid } from '@eemp/ui';
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

interface Kpi {
  key: string;
  href: string;
  labelKey: string;
  count: (api: Endpoints) => Promise<number>;
}

/** Headline counts for what the user can read (each a cheap pageSize=1 query). */
function kpisFor(user: CurrentUser, can: (p: string) => boolean): Kpi[] {
  if (user.isPlatformAdmin) {
    return [{ key: 'tenants', href: '/tenants', labelKey: 'tenants', count: async (api) => (await api.platform.tenants.list({ pageSize: 1 })).totalCount }];
  }
  const list: Kpi[] = [];
  if (can('students.read')) list.push({ key: 'students', href: '/people/students', labelKey: 'students', count: async (api) => (await api.people('students').list({ pageSize: 1 })).totalCount });
  if (can('teachers.read')) list.push({ key: 'teachers', href: '/people/teachers', labelKey: 'teachers', count: async (api) => (await api.people('teachers').list({ pageSize: 1 })).totalCount });
  if (can('branches.read')) list.push({ key: 'branches', href: '/branches', labelKey: 'branches', count: async (api) => (await api.branches.list({ pageSize: 1 })).totalCount });
  if (can('users.read')) list.push({ key: 'users', href: '/users', labelKey: 'users', count: async (api) => (await api.users.list({ pageSize: 1 })).totalCount });
  return list;
}

function KpiCard({ kpi }: { kpi: Kpi }) {
  const t = useTranslations('dashboard');
  const nav = useTranslations('nav');
  const format = useLocaleFormat();
  const query = useApiQuery(`dashboard-kpi-${kpi.key}`, kpi.count);
  return (
    <StatCard
      data-testid={`kpi-${kpi.key}`}
      label={nav(kpi.labelKey)}
      loading={query.loading && query.data === undefined}
      value={query.data === undefined ? '—' : format.number(query.data)}
      footer={
        <Link href={kpi.href} style={{ display: 'inline-flex', alignItems: 'center', gap: 4, fontWeight: 600 }}>
          {t('kpiViewAll')}
          <span className="eemp-visually-hidden">{nav(kpi.labelKey)}</span>
          <IconChevron size={16} />
        </Link>
      }
    />
  );
}

export function DashboardScreen() {
  const t = useTranslations('dashboard');
  const nav = useTranslations('nav');
  const { user, can } = useSession();
  const branches = useApiQuery('dashboard-branches', async (api) =>
    can('branches.read') ? (await api.branches.list({ pageSize: 100 })).items : ([] as Branch[]),
  );
  const format = useLocaleFormat();
  const links = adminNavigation(user).flatMap((s) => s.items).filter((i) => i.href !== '/');
  const kpis = kpisFor(user, can);
  return (
    <>
      <PageHeader title={t('welcome', { name: user.displayName })} description={user.isPlatformAdmin ? t('platformIntro') : t('tenantIntro')} />
      <section aria-labelledby="kpi-heading">
        <h2 id="kpi-heading" className="eemp-visually-hidden">{t('kpiTitle')}</h2>
        <StatGrid>
          {kpis.map((k) => (
            <KpiCard key={k.key} kpi={k} />
          ))}
          {!user.isPlatformAdmin && (
            <StatCard data-testid="kpi-permissions" label={t('kpiPermissions')} value={format.number(user.permissions.length)} />
          )}
        </StatGrid>
      </section>
      <div style={{ display: 'grid', gap: 20, gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 18rem), 1fr))', alignItems: 'start' }}>
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
          <h2 id="links-heading" style={{ fontSize: '1.0625rem', marginBlock: '0 1.25rem' }}>{t('quickLinks')}</h2>
          <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'grid', gap: 12, gridTemplateColumns: 'repeat(auto-fill, minmax(min(100%, 12rem), 1fr))' }}>
            {links.map((l) => (
              <li key={l.href}>
                <Link href={l.href} className="eemp-tile" style={{ minBlockSize: '3.25rem', padding: '0.625rem 1rem' }}>
                  <span>{nav(l.labelKey)}</span>
                  <IconChevron size={16} />
                </Link>
              </li>
            ))}
          </ul>
        </Card>
      </div>
    </>
  );
}
