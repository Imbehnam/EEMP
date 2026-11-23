'use client';

import { keyframes } from '@emotion/react';
import styled from '@emotion/styled';
import type { HTMLAttributes, ReactNode } from 'react';

/** Hides content visually while keeping it available to assistive technology. */
export const VisuallyHidden = styled.span`
  position: absolute !important;
  inline-size: 1px;
  block-size: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
`;

export const SkipLink = styled.a`
  position: absolute;
  inset-inline-start: ${({ theme }) => theme.space[4]};
  inset-block-start: -100px;
  z-index: ${({ theme }) => theme.zIndices.toast};
  padding: ${({ theme }) => `${theme.space[2]} ${theme.space[4]}`};
  background: ${({ theme }) => theme.colors.primary};
  color: ${({ theme }) => theme.colors.primaryFg};
  border-radius: ${({ theme }) => theme.radii.md};
  font-weight: ${({ theme }) => theme.fontWeights.semibold};
  &:focus {
    inset-block-start: ${({ theme }) => theme.space[2]};
  }
`;

const spin = keyframes`to { transform: rotate(360deg); }`;

const SpinnerCircle = styled.span<{ size: number }>`
  display: inline-block;
  inline-size: ${({ size }) => size}px;
  block-size: ${({ size }) => size}px;
  border: 2px solid currentColor;
  border-inline-end-color: transparent;
  border-radius: 9999px;
  animation: ${spin} 0.7s linear infinite;
  vertical-align: middle;
`;

export interface SpinnerProps {
  /** Accessible label, e.g. t('common.loading'). Omit when a parent already announces busy state. */
  label?: string;
  size?: number;
}

export function Spinner({ label, size = 16 }: SpinnerProps) {
  if (!label) return <SpinnerCircle size={size} aria-hidden="true" />;
  return (
    <span role="status" style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
      <SpinnerCircle size={size} aria-hidden="true" />
      <VisuallyHidden>{label}</VisuallyHidden>
    </span>
  );
}

export const Card = styled.section`
  background: ${({ theme }) => theme.colors.surface};
  border: 1px solid ${({ theme }) => theme.colors.border};
  border-radius: ${({ theme }) => theme.radii.lg};
  box-shadow: ${({ theme }) => theme.shadows.card};
  padding: ${({ theme }) => theme.space[6]};
`;

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

const toneColors = (tone: Tone) =>
  ({
    neutral: ['var(--eemp-color-surface-muted)', 'var(--eemp-color-fg)'],
    info: ['var(--eemp-color-info-soft)', 'var(--eemp-color-info)'],
    success: ['var(--eemp-color-success-soft)', 'var(--eemp-color-success)'],
    warning: ['var(--eemp-color-warning-soft)', 'var(--eemp-color-warning)'],
    danger: ['var(--eemp-color-danger-soft)', 'var(--eemp-color-danger)'],
  })[tone];

export const Badge = styled.span<{ tone?: Tone }>`
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.125rem 0.625rem;
  border-radius: 9999px;
  font-size: ${({ theme }) => theme.fontSizes.xs};
  font-weight: ${({ theme }) => theme.fontWeights.semibold};
  background: ${({ tone = 'neutral' }) => toneColors(tone)[0]};
  color: ${({ tone = 'neutral' }) => toneColors(tone)[1]};
  white-space: nowrap;
`;

const AlertBox = styled.div<{ tone: Tone }>`
  display: flex;
  flex-direction: column;
  gap: ${({ theme }) => theme.space[1]};
  padding: ${({ theme }) => `${theme.space[3]} ${theme.space[4]}`};
  border-radius: ${({ theme }) => theme.radii.md};
  border-inline-start: 4px solid ${({ tone }) => toneColors(tone)[1]};
  background: ${({ tone }) => toneColors(tone)[0]};
  color: ${({ theme }) => theme.colors.fg};
  & > strong {
    color: ${({ tone }) => toneColors(tone)[1]};
  }
`;

export interface AlertProps extends Omit<HTMLAttributes<HTMLDivElement>, 'title'> {
  tone?: Tone;
  title?: ReactNode;
  action?: ReactNode;
  children?: ReactNode;
}

/** Danger/warning alerts are announced assertively (role=alert); others politely (role=status). */
export function Alert({ tone = 'info', title, action, children, ...rest }: AlertProps) {
  const role = tone === 'danger' || tone === 'warning' ? 'alert' : 'status';
  return (
    <AlertBox tone={tone} role={role} {...rest}>
      {title && <strong>{title}</strong>}
      {children && <div>{children}</div>}
      {action && <div style={{ marginBlockStart: 8 }}>{action}</div>}
    </AlertBox>
  );
}

const HeaderRow = styled.div`
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  justify-content: space-between;
  gap: ${({ theme }) => theme.space[4]};
  margin-block-end: ${({ theme }) => theme.space[6]};
  h1 {
    margin: 0;
    font-size: ${({ theme }) => theme.fontSizes['2xl']};
    line-height: 1.3;
  }
  p {
    margin: ${({ theme }) => theme.space[1]} 0 0;
    color: ${({ theme }) => theme.colors.fgMuted};
  }
`;

export interface PageHeaderProps {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  /** Optional breadcrumb/back link rendered above the title. */
  before?: ReactNode;
}

export function PageHeader({ title, description, actions, before }: PageHeaderProps) {
  return (
    <HeaderRow>
      <div>
        {before}
        <h1>{title}</h1>
        {description && <p>{description}</p>}
      </div>
      {actions && <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>{actions}</div>}
    </HeaderRow>
  );
}

const StateBox = styled.div`
  text-align: center;
  padding: ${({ theme }) => `${theme.space[10]} ${theme.space[4]}`};
  color: ${({ theme }) => theme.colors.fgMuted};
  h2 {
    margin: 0 0 ${({ theme }) => theme.space[2]};
    color: ${({ theme }) => theme.colors.fg};
    font-size: ${({ theme }) => theme.fontSizes.lg};
  }
  p {
    margin: 0 auto;
    max-inline-size: 36rem;
  }
`;

export interface StatePanelProps {
  title: ReactNode;
  description?: ReactNode;
  action?: ReactNode;
  /** Use "alert" for error states so screen readers announce them. */
  role?: 'alert' | 'status';
}

/** Empty, forbidden, not-found and error states. */
export function StatePanel({ title, description, action, role }: StatePanelProps) {
  return (
    <StateBox role={role}>
      <h2>{title}</h2>
      {description && <p>{description}</p>}
      {action && <div style={{ marginBlockStart: 16 }}>{action}</div>}
    </StateBox>
  );
}

export const DescriptionList = styled.dl`
  display: grid;
  grid-template-columns: minmax(8rem, max-content) 1fr;
  gap: ${({ theme }) => `${theme.space[2]} ${theme.space[6]}`};
  margin: 0;
  dt {
    color: ${({ theme }) => theme.colors.fgMuted};
    font-size: ${({ theme }) => theme.fontSizes.sm};
  }
  dd {
    margin: 0;
    font-weight: ${({ theme }) => theme.fontWeights.medium};
    overflow-wrap: anywhere;
  }
`;
