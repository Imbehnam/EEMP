'use client';

import styled from '@emotion/styled';
import type { ReactNode } from 'react';

import { Skeleton } from './primitives';

const Box = styled.div`
  display: flex;
  flex-direction: column;
  gap: ${({ theme }) => theme.space[1]};
  min-inline-size: 0;
  padding: ${({ theme }) => `${theme.space[5]} ${theme.space[6]}`};
  border-radius: ${({ theme }) => theme.radii.lg};
  border: 1px solid var(--eemp-card-edge, transparent);
  background: ${({ theme }) => theme.colors.surface};
  box-shadow: ${({ theme }) => theme.shadows.raised};
  .eemp-stat-label {
    margin: 0;
    color: ${({ theme }) => theme.colors.fgMuted};
    font-size: ${({ theme }) => theme.fontSizes.sm};
    font-weight: ${({ theme }) => theme.fontWeights.semibold};
  }
  .eemp-stat-value {
    margin: 0;
    font-size: ${({ theme }) => theme.fontSizes['3xl']};
    font-weight: ${({ theme }) => theme.fontWeights.bold};
    line-height: 1.2;
    font-variant-numeric: tabular-nums;
    color: ${({ theme }) => theme.colors.fg};
  }
  .eemp-stat-foot {
    margin-block-start: ${({ theme }) => theme.space[2]};
    font-size: ${({ theme }) => theme.fontSizes.sm};
  }
`;

export interface StatCardProps {
  label: ReactNode;
  /** Already-formatted value (locale digits). */
  value?: ReactNode;
  loading?: boolean;
  /** Footer: usually a link to the list behind the number. */
  footer?: ReactNode;
  'data-testid'?: string;
}

/** Dashboard KPI: label, big tabular number, and a next-step link. */
export function StatCard({ label, value, loading, footer, ...rest }: StatCardProps) {
  return (
    <Box {...rest}>
      <p className="eemp-stat-label">{label}</p>
      {loading ? <Skeleton width="4rem" height="2.25rem" /> : <p className="eemp-stat-value">{value}</p>}
      {footer && <div className="eemp-stat-foot">{footer}</div>}
    </Box>
  );
}

/** Responsive KPI grid. */
export const StatGrid = styled.div`
  display: grid;
  gap: ${({ theme }) => theme.space[5]};
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 13rem), 1fr));
  margin-block-end: ${({ theme }) => theme.space[6]};
`;
