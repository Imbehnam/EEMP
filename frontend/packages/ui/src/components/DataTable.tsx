'use client';

import styled from '@emotion/styled';
import type { ReactNode } from 'react';

import { Spinner } from './primitives';

const Scroller = styled.div`
  overflow-x: auto;
  border: 1px solid ${({ theme }) => theme.colors.border};
  border-radius: ${({ theme }) => theme.radii.lg};
  background: ${({ theme }) => theme.colors.surface};
  &:focus-visible {
    outline: 3px solid ${({ theme }) => theme.colors.focus};
  }
`;

const Table = styled.table`
  inline-size: 100%;
  border-collapse: collapse;
  font-size: ${({ theme }) => theme.fontSizes.sm};
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
  th {
    background: ${({ theme }) => theme.colors.surfaceMuted};
    font-weight: ${({ theme }) => theme.fontWeights.semibold};
    white-space: nowrap;
  }
  tbody tr:last-of-type td {
    border-block-end: none;
  }
  tbody tr:hover {
    background: ${({ theme }) => theme.colors.surfaceMuted};
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
            <tr>
              <td colSpan={columns.length} style={{ textAlign: 'center', padding: 32 }}>
                <Spinner label={loadingLabel} size={20} />
              </td>
            </tr>
          ) : rows.length === 0 ? (
            <tr>
              <td colSpan={columns.length}>{empty}</td>
            </tr>
          ) : (
            rows.map((row) => (
              <tr key={getRowKey(row)}>
                {columns.map((c) =>
                  c.isRowHeader ? (
                    <th key={c.key} scope="row" style={{ textAlign: c.align ?? 'start', background: 'transparent' }}>
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
    </Scroller>
  );
}
