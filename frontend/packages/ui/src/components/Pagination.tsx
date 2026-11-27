'use client';

import styled from '@emotion/styled';

import { Button } from './Button';
import { IconChevron } from './direction';

const Nav = styled.nav`
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: ${({ theme }) => theme.space[3]};
  margin-block-start: ${({ theme }) => theme.space[4]};
  color: ${({ theme }) => theme.colors.fgMuted};
  font-size: ${({ theme }) => theme.fontSizes.sm};
`;

export interface PaginationLabels {
  /** aria-label of the <nav>, e.g. "Pagination". */
  nav: string;
  previous: string;
  next: string;
  /** Already-formatted status, e.g. "Showing 1–20 of 57". */
  summary: string;
}

export interface PaginationProps {
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  labels: PaginationLabels;
}

export function pageCount(totalCount: number, pageSize: number) {
  return Math.max(1, Math.ceil(totalCount / Math.max(1, pageSize)));
}

export function Pagination({ page, pageSize, totalCount, onPageChange, labels }: PaginationProps) {
  const pages = pageCount(totalCount, pageSize);
  return (
    <Nav aria-label={labels.nav}>
      <span aria-live="polite">{labels.summary}</span>
      <div style={{ display: 'flex', gap: 8 }}>
        <Button variant="secondary" size="sm" onClick={() => onPageChange(page - 1)} disabled={page <= 1}>
          <IconChevron size={16} towards="start" />
          {labels.previous}
        </Button>
        <Button variant="secondary" size="sm" onClick={() => onPageChange(page + 1)} disabled={page >= pages}>
          {labels.next}
          <IconChevron size={16} />
        </Button>
      </div>
    </Nav>
  );
}
