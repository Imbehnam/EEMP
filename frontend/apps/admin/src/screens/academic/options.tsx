'use client';

import type { AcademicYear, Branch, Course, GradingScheme, Subject, Term } from '@eemp/api-client';
import { useApiQuery, useSession } from '@eemp/app';
import { useLocaleFormat } from '@eemp/i18n/react';
import { useTranslations } from 'next-intl';

/** Lookup lists for academic forms and filters (first 100 of each; UX only — the API validates ids). */
export function useAcademicLookups() {
  const { can } = useSession();
  const catalog = can('catalog.read');
  const years = useApiQuery<AcademicYear[]>(`lk-years|${catalog}`, async (api) => (catalog ? (await api.academic.years.list({ pageSize: 100 })).items : []));
  const terms = useApiQuery<Term[]>(`lk-terms|${catalog}`, async (api) => (catalog ? api.academic.terms.list() : []));
  const subjects = useApiQuery<Subject[]>(`lk-subjects|${catalog}`, async (api) => (catalog ? (await api.academic.subjects.list({ pageSize: 100 })).items : []));
  const courses = useApiQuery<Course[]>(`lk-courses|${catalog}`, async (api) => (catalog ? (await api.academic.courses.list({ pageSize: 100 })).items : []));
  const schemes = useApiQuery<GradingScheme[]>(`lk-schemes|${catalog}`, async (api) => (catalog ? api.academic.gradingSchemes.list() : []));
  const branches = useApiQuery<Branch[]>(`lk-branches|${can('branches.read')}`, async (api) =>
    can('branches.read') ? (await api.branches.list({ pageSize: 100 })).items : [],
  );
  return {
    years: years.data ?? [],
    terms: terms.data ?? [],
    subjects: subjects.data ?? [],
    courses: courses.data ?? [],
    schemes: schemes.data ?? [],
    branches: branches.data ?? [],
    loading: years.loading || terms.loading || subjects.loading || courses.loading || schemes.loading,
  };
}

/** "1405-1406 · Term 1" style label for a term. */
export function useTermLabel() {
  const lookups = useAcademicLookups();
  return (termId: string) => {
    const term = lookups.terms.find((t) => t.id === termId);
    if (!term) return '';
    const year = lookups.years.find((y) => y.id === term.academicYearId);
    return year ? `${year.name} · ${term.name}` : term.name;
  };
}

export function DateRange({ start, end }: { start: string; end: string }) {
  const format = useLocaleFormat();
  const t = useTranslations('academic.common');
  return <>{t('dateRange', { start: format.calendarDate(start), end: format.calendarDate(end) })}</>;
}
