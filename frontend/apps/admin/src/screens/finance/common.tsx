'use client';

import type { Student, TenantUser } from '@eemp/api-client';
import { useApiQuery, useSession } from '@eemp/app';
import { SearchBox, SelectField } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useState } from 'react';

/** Student search + select (students.read). The API re-checks finance scope for whatever is submitted. */
export function StudentPicker({
  value, onChange, error, required, initialLabel,
}: { value: string; onChange: (studentId: string, student?: Student) => void; error?: string; required?: boolean; initialLabel?: string }) {
  const t = useTranslations('finance.common');
  const [search, setSearch] = useState('');
  const students = useApiQuery(`fin-students|${search}`, async (api) => (await api.people('students').list({ search, pageSize: 20 })).items);
  const options = (students.data ?? []).map((s) => ({ value: s.id, label: `${s.firstName} ${s.lastName} (${s.studentNumber ?? ''})` }));
  if (value && initialLabel && !options.some((o) => o.value === value)) options.unshift({ value, label: initialLabel });
  return (
    <div style={{ display: 'grid', gap: 8 }}>
      <SearchBox label={t('searchStudent')} onSearch={setSearch} />
      <SelectField
        label={t('student')}
        name="studentId"
        required={required}
        value={value}
        error={error}
        placeholder={options.length ? t('chooseStudent') : t('noStudentFound')}
        onChange={(e) => onChange(e.target.value, students.data?.find((s) => s.id === e.target.value))}
        options={options}
      />
    </div>
  );
}

/** Display names of tenant users (users.read; otherwise "You" or a short id). Ids only ever come from the API. */
export function useUserNames() {
  const { user, can } = useSession();
  const t = useTranslations('finance.common');
  const users = useApiQuery<TenantUser[]>(`fin-users|${can('users.read')}`, async (api) => (can('users.read') ? (await api.users.list({ pageSize: 100 })).items : []));
  return (id: string | null | undefined) => {
    if (!id) return t('none');
    if (id === user.id) return t('you');
    return users.data?.find((u) => u.id === id)?.displayName ?? `${id.slice(0, 8)}…`;
  };
}

/** Branch names for branch-scoped rows (branches.read; falls back to nothing). */
export function useBranchNames() {
  const { can } = useSession();
  const branches = useApiQuery(`fin-branches|${can('branches.read')}`, async (api) => (can('branches.read') ? (await api.branches.list({ pageSize: 100 })).items : []));
  return {
    list: branches.data ?? [],
    name: (id: string | null | undefined) => (id ? (branches.data?.find((b) => b.id === id)?.name ?? '') : ''),
  };
}

export const sectionTitle = { fontSize: '1.0625rem', marginBlockStart: 0 } as const;

/** The tenant currency (finance settings; the server stamps it on every record). Falls back to IRR, the M1 default. */
export function useTenantCurrency(): string {
  const settings = useApiQuery('fin-currency', async (api) => {
    try {
      return (await api.finance.settings.get()).currency;
    } catch {
      return 'IRR';
    }
  });
  return settings.data ?? 'IRR';
}
