'use client';

import styled from '@emotion/styled';
import type { ReactNode } from 'react';

import { Skeleton, VisuallyHidden } from './primitives';

/**
 * Raised table card. The scroller caps its height so the header row can stay sticky while rows
 * scroll; hairline row separators + subtle zebra keep rows readable without relying on depth.
 */
const Scroller = styled.div`
  overflow: auto;
  max-block-size: min(72vh, 52rem);
  border: 1px solid var(--eemp-card-edge, transparent);
  border-radius: ${({ theme }) => theme.radii.lg};
  background: ${({ theme }) => theme.colors.surface};
  box-shadow: ${({ theme }) => theme.shadows.raised};
  &:focus-visible {
    outline: 3px solid ${({ theme }) => theme.colors.focus};
    outline-offset: 2px;
  }
  @media print {
    max-block-size: none;
    box-shadow: none;
  }
`;

const Table = styled.table`
  inline-size: 100%;
  border-collapse: separate;
  border-spacing: 0;
  font-size: ${({ theme }) => theme.fontSizes.sm};
  font-variant-numeric: tabular-nums;
  caption {
    text-align: start;
    padding: ${({ theme }) => `${theme.space[3]} ${theme.space[4]}`};
    font-weight: ${({ theme }) => theme.fontWeights.semibold};
  }
  th,
  td {
    text-align: start;
    padding: ${({ theme }) => `${theme.space[3]} ${theme.space[4]}`};
    border-block-end: 1px solid ${({ theme }) => theme.colors.border};
    vertical-align: middle;
  }
  thead th {
    position: sticky;
    inset-block-start: 0;
    z-index: 1;
    background: ${({ theme }) => theme.colors.surfaceMuted};
    border-block-end-color: ${({ theme }) => theme.colors.borderStrong};
    color: ${({ theme }) => theme.colors.fg};
    font-size: ${({ theme }) => theme.fontSizes.xs};
    font-weight: ${({ theme }) => theme.fontWeights.bold};
    letter-spacing: 0.02em;
    white-space: nowrap;
  }
  tbody tr:nth-of-type(even) {
    background: color-mix(in srgb, ${({ theme }) => theme.colors.surfaceMuted} 45%, transparent);
  }
  tbody tr:last-of-type > * {
    border-block-end: none;
  }
  tbody tr:hover {
    background: ${({ theme }) => theme.colors.primarySoft};
  }
  tbody th {
    font-weight: ${({ theme }) => theme.fontWeights.semibold};
  }
`;

export interface Column<T> {
  key: string;
  header: ReactNode;
  cell: (row: T) => ReactNode;
  /** Mark the column that names the row; rendered as <th scope="row"> for screen readers. */
  isRowHeader?: boolean;
  align?: 'start' | 'end' | 'center';
}

export interface DataTableProps<T> {
  caption: string;
  /** Hide the caption visually but keep it for assistive tech. */
  captionHidden?: boolean;
  columns: Column<T>[];
  rows: T[];
  getRowKey: (row: T) => string;
  loading?: boolean;
  loadingLabel?: string;
  empty?: ReactNode;
}

export function DataTable<T>({
  caption, captionHidden = true, columns, rows, getRowKey, loading, loadingLabel, empty,
}: DataTableProps<T>) {
  return (
    <Scroller role="region" aria-label={caption} tabIndex={0} aria-busy={loading || undefined}>
      <Table>
        <caption style={captionHidden ? { position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0,0,0,0)' } : undefined}>
          {caption}
        </caption>
        <thead>
          <tr>
            {columns.map((c) => (
              <th key={c.key} scope="col" style={{ textAlign: c.align ?? 'start' }}>
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {loading && rows.length === 0 ? (
            Array.from({ length: 4 }, (_, r) => (
              <tr key={`skeleton-${r}`} aria-hidden="true">
                {columns.map((c, i) => (
                  <td key={c.key}>
                    <Skeleton width={i === 0 ? '70%' : `${40 + ((r + i) % 3) * 15}%`} />
                  </td>
                ))}
              </tr>
            ))
          ) : rows.length === 0 ? (
            <tr>
              <td colSpan={columns.length}>{empty}</td>
            </tr>
          ) : (
            rows.map((row) => (
              <tr key={getRowKey(row)}>
                {columns.map((c) =>
                  c.isRowHeader ? (
                    <th key={c.key} scope="row" style={{ textAlign: c.align ?? 'start' }}>
                      {c.cell(row)}
                    </th>
                  ) : (
                    <td key={c.key} style={{ textAlign: c.align ?? 'start' }}>
                      {c.cell(row)}
                    </td>
                  ),
                )}
              </tr>
            ))
          )}
        </tbody>
      </Table>
      {loading && rows.length === 0 && loadingLabel && (
        <VisuallyHidden role="status">{loadingLabel}</VisuallyHidden>
      )}
    </Scroller>
  );
}
