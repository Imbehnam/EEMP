'use client';

import type { PersonBase, PortalKind, PortalProfile, PortalStudent, Staff, Student, Teacher } from '@eemp/api-client';
import { LoadError, PreferencesForm, useApiQuery, useSession } from '@eemp/app';
import { Link } from '@eemp/i18n/navigation';
import { useLocaleFormat } from '@eemp/i18n/react';
import { Badge, Card, DescriptionList, PageHeader, Spinner, StatePanel } from '@eemp/ui';
import { useTranslations } from 'next-intl';


export function PortalHomeScreen({ kind }: { kind: PortalKind | null }) {
  const t = useTranslations('portal');
  const shell = useTranslations('shell');
  const { user } = useSession();
  const cards = [
    { href: '/profile', label: t('cardProfile') },
    ...(kind === 'guardian' ? [{ href: '/students', label: t('cardStudents') }] : []),
    ...(kind === 'teacher' ? [{ href: '/classes', label: t('cardClasses') }] : []),
    ...(kind === 'student' || kind === 'guardian'
      ? [
          { href: '/timetable', label: t('cardTimetable') },
          { href: '/grades', label: t('cardGrades') },
          { href: '/invoices', label: t('cardInvoices') },
        ]
      : []),
    { href: '/settings', label: t('cardSettings') },
  ];
  return (
    <>
      <PageHeader
        title={t('welcome', { name: user.displayName })}
        description={user.tenantName ? t('intro', { tenant: user.tenantName }) : t('introNoTenant')}
      />
      <div style={{ display: 'grid', gap: 16, gridTemplateColumns: 'repeat(auto-fit, minmax(16rem, 1fr))' }}>
        <Card aria-labelledby="roles-heading">
          <h2 id="roles-heading" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('yourRoles')}</h2>
          {kind && <p><Badge tone="info">{t('kind', { kind })}</Badge></p>}
          {user.scopes.length === 0 ? (
            <p>{t('noRoles')}</p>
          ) : (
            <ul style={{ margin: 0, paddingInlineStart: 20 }}>
              {user.scopes.map((s, i) => (
                <li key={`${s.roleCode}-${i}`}>
                  <span className="eemp-ltr">{s.roleCode}</span>
                  {s.branchId === null && <> · {shell('tenantWide')}</>}
                </li>
              ))}
            </ul>
          )}
        </Card>
        {cards.map((c) => (
          <Card key={c.href}>
            <Link href={c.href} style={{ fontWeight: 600 }}>{c.label}</Link>
          </Card>
        ))}
      </div>
    </>
  );
}

/** Read-only person record (own profile or a linked student). */
export function PersonDetails({ person, kind }: { person: PersonBase; kind: PortalKind }) {
  const t = useTranslations('people');
  const common = useTranslations('common');
  const format = useLocaleFormat();
  const none = common('none');
  return (
    <DescriptionList>
      <dt>{t('fullName')}</dt>
      <dd>{`${person.firstName} ${person.lastName}`}</dd>
      {kind === 'student' && (
        <>
          <dt>{t('studentNumber')}</dt>
          <dd className="eemp-ltr">{(person as Student).studentNumber}</dd>
        </>
      )}
      {(kind === 'teacher' || kind === 'staff') && (
        <>
          <dt>{t('employeeNumber')}</dt>
          <dd className="eemp-ltr">{(person as Teacher).employeeNumber}</dd>
        </>
      )}
      {kind === 'teacher' && (
        <>
          <dt>{t('subjects')}</dt>
          <dd>{(person as Teacher).subjects || none}</dd>
        </>
      )}
      {kind === 'staff' && (
        <>
          <dt>{t('position')}</dt>
          <dd>{(person as Staff).position}</dd>
        </>
      )}
      <dt>{t('birthDate')}</dt>
      <dd data-testid="birth-date">{person.birthDate ? format.calendarDate(person.birthDate) : none}</dd>
      <dt>{t('gender')}</dt>
      <dd>{person.gender ? t('genderLabel', { gender: person.gender }) : t('unspecified')}</dd>
      <dt>{t('nationalId')}</dt>
      <dd className="eemp-ltr">{person.nationalId || none}</dd>
      <dt>{t('phone')}</dt>
      <dd className="eemp-ltr">{person.phone || none}</dd>
      <dt>{t('email')}</dt>
      <dd className="eemp-ltr">{person.email || none}</dd>
      <dt>{t('status')}</dt>
      <dd>{t('statusLabel', { status: person.status })}</dd>
    </DescriptionList>
  );
}

export function ProfileScreen() {
  const t = useTranslations('portal');
  const common = useTranslations('common');
  const query = useApiQuery<PortalProfile>('portal-profile', (api) => api.portal.profile());
  return (
    <>
      <PageHeader title={t('profileTitle')} description={t('profileIntro')} />
      {query.error ? (
        <LoadError error={query.error} onRetry={query.reload} />
      ) : !query.data ? (
        <Spinner label={common('loading')} />
      ) : (
        <Card>
          <p style={{ marginBlockStart: 0 }}>
            <Badge tone="info">{t('kind', { kind: query.data.kind })}</Badge>
          </p>
          <PersonDetails person={query.data.profile} kind={query.data.kind} />
        </Card>
      )}
    </>
  );
}

/** Linked-student summary from GET /portal/me/students (the guardian sees a summary, not the full record). */
function LinkedStudentDetails({ student }: { student: PortalStudent }) {
  const t = useTranslations('people');
  return (
    <DescriptionList>
      <dt>{t('fullName')}</dt>
      <dd>{`${student.firstName} ${student.lastName}`}</dd>
      <dt>{t('studentNumber')}</dt>
      <dd className="eemp-ltr">{student.studentNumber}</dd>
      <dt>{t('relationship')}</dt>
      <dd>{t('relationshipLabel', { relationship: student.relationship })}</dd>
    </DescriptionList>
  );
}

export function MyStudentsScreen() {
  const t = useTranslations('portal');
  const common = useTranslations('common');
  const { user } = useSession();
  const query = useApiQuery('portal-students', (api) => api.portal.students());
  return (
    <>
      <PageHeader title={t('myStudentsTitle')} description={t('myStudentsIntro')} />
      {query.error ? (
        <LoadError error={query.error} onRetry={query.reload} />
      ) : !query.data ? (
        <Spinner label={common('loading')} />
      ) : query.data.length === 0 ? (
        <StatePanel title={t('noStudents')} />
      ) : (
        <div style={{ display: 'grid', gap: 16 }} data-user={user.id}>
          {query.data.map((student) => (
            <Card key={student.id} aria-label={`${student.firstName} ${student.lastName}`}>
              {student.isPrimary && <p style={{ marginBlockStart: 0 }}><Badge tone="success">{t('primaryContact')}</Badge></p>}
              <LinkedStudentDetails student={student} />
            </Card>
          ))}
        </div>
      )}
    </>
  );
}

export function PortalSettingsScreen({ saved }: { saved?: boolean }) {
  const t = useTranslations('settings');
  return (
    <>
      <PageHeader title={t('title')} description={t('intro')} />
      <PreferencesForm saved={saved} />
    </>
  );
}
