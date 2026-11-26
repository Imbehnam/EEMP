'use client';

import type { Endpoints, PageQuery, Paged } from '@eemp/api-client';
import { ButtonLink, LoadError, toApiErrorLike, useApiQuery } from '@eemp/app';
import { useErrorTranslator, useLocaleFormat } from '@eemp/i18n/react';
import { Link } from '@eemp/i18n/navigation';
import { Badge, Button, DataTable, IconChevron, Pagination, SearchBox, StatePanel, useToast, type Column } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useEffect, useRef, useState, type ReactNode } from 'react';

export const PAGE_SIZE = 20;

/** Translated field errors from a problem+json `errors` map. */
export function useFieldErrors(error: unknown): Record<string, string> {
  const errors = useErrorTranslator();
  if (!error) return {};
  return errors.fields(toApiErrorLike(error).fieldErrors);
}

export function ActiveBadge({ active }: { active: boolean }) {
  const t = useTranslations('common');
  return <Badge tone={active ? 'success' : 'neutral'}>{active ? t('active') : t('inactive')}</Badge>;
}

/** One-shot confirmation after a redirect (e.g. `?notice=created`), shown as a toast. */
export function Notice({ notice }: { notice?: string }) {
  const t = useTranslations('common');
  const toast = useToast();
  const shown = useRef<string | undefined>(undefined);
  const known = notice === 'created' || notice === 'saved' || notice === 'deleted';
  useEffect(() => {
    if (!known || shown.current === notice) return;
    shown.current = notice;
    toast.show(t(notice));
  }, [known, notice, t, toast]);
  return null;
}

export function BackLink({ href }: { href: string }) {
  const t = useTranslations('common');
  return (
    <Link href={href} style={{ fontSize: '0.875rem' }}>
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
        <IconChevron size={16} towards="start" />
        {t('backToList')}
      </span>
    </Link>
  );
}

export function CreateButton({ href, label }: { href: string; label: string }) {
  return <ButtonLink href={href}>{label}</ButtonLink>;
}

export interface PagedListProps<T> {
  /** Distinguishes cached queries (e.g. "branches"). */
  queryKey: string;
  load: (api: Endpoints, query: PageQuery) => Promise<Paged<T>>;
  columns: Column<T>[];
  getRowKey: (row: T) => string;
  caption: string;
  searchLabel: string;
  emptyText: string;
}

/** Server-paged, searchable table with loading, empty, error and forbidden states. */
export function PagedList<T>({ queryKey, load, columns, getRowKey, caption, searchLabel, emptyText }: PagedListProps<T>) {
  const t = useTranslations('common');
  const format = useLocaleFormat();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const query = useApiQuery(`${queryKey}|${page}|${search}`, (api) => load(api, { page, pageSize: PAGE_SIZE, search }));

  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const data = query.data;
  const from = data && data.totalCount > 0 ? (data.page - 1) * data.pageSize + 1 : 0;
  const to = data ? Math.min(data.totalCount, from + data.items.length - 1) : 0;
  return (
    <>
      <div style={{ marginBlockEnd: 12 }}>
        <SearchBox
          label={searchLabel}
          placeholder={t('searchPlaceholder')}
          onSearch={(value) => {
            setSearch(value);
            setPage(1);
          }}
        />
      </div>
      <DataTable
        caption={caption}
        columns={columns}
        rows={data?.items ?? []}
        getRowKey={getRowKey}
        loading={query.loading}
        loadingLabel={t('loading')}
        empty={<StatePanel title={search ? t('noResults', { query: search }) : emptyText} />}
      />
      {data && data.totalCount > 0 && (
        <Pagination
          page={data.page}
          pageSize={data.pageSize}
          totalCount={data.totalCount}
          onPageChange={setPage}
          labels={{
            nav: t('pagination.label'),
            previous: t('pagination.previous'),
            next: t('pagination.next'),
            summary: t('pagination.summary', {
              from: format.number(from),
              to: format.number(to),
              total: format.number(data.totalCount),
            }),
          }}
        />
      )}
    </>
  );
}

export function FormActions({ cancelHref, busy, children }: { cancelHref: string; busy?: boolean; children?: ReactNode }) {
  const t = useTranslations('common');
  return (
    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBlockStart: 20 }}>
      <ButtonSubmit busy={busy} label={busy ? t('saving') : t('save')} />
      <ButtonLink href={cancelHref} variant="secondary">{t('cancel')}</ButtonLink>
      {children}
    </div>
  );
}

function ButtonSubmit({ busy, label }: { busy?: boolean; label: string }) {
  return <Button type="submit" loading={busy}>{label}</Button>;
}

/** Empty string -> null for optional API fields. */
export const nullIfEmpty = (value: string) => (value.trim() === '' ? null : value.trim());
