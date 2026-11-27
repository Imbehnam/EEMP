'use client';

import { keyframes } from '@emotion/react';
import styled from '@emotion/styled';
import type { HTMLAttributes, ReactNode } from 'react';

import { IconEmpty, toneIcons } from './icons';

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

/** Raised soft surface. A faint edge keeps the boundary legible where shadows are subtle (dark theme, print). */
export const Card = styled.section`
  background: ${({ theme }) => theme.colors.surface};
  border: 1px solid var(--eemp-card-edge, transparent);
  border-radius: ${({ theme }) => theme.radii.lg};
  box-shadow: ${({ theme }) => theme.shadows.raised};
  padding: ${({ theme }) => theme.space[6]};
  min-inline-size: 0;
  h2:first-of-type {
    margin-block-start: 0;
  }
  @media print {
    box-shadow: none;
    border-color: ${({ theme }) => theme.colors.border};
  }
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

/** Status chip: tinted pill + leading dot; the text always names the status (colour is never the only cue). */
export const Badge = styled.span<{ tone?: Tone }>`
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.1875rem 0.625rem;
  border-radius: 9999px;
  font-size: ${({ theme }) => theme.fontSizes.xs};
  font-weight: ${({ theme }) => theme.fontWeights.semibold};
  line-height: 1.5;
  background: ${({ tone = 'neutral' }) => toneColors(tone)[0]};
  color: ${({ tone = 'neutral' }) => toneColors(tone)[1]};
  box-shadow: inset 0 0 0 1px color-mix(in srgb, currentColor 22%, transparent);
  white-space: nowrap;
  &::before {
    content: '';
    inline-size: 0.4375rem;
    block-size: 0.4375rem;
    border-radius: 9999px;
    background: currentColor;
    flex: none;
  }
`;

const AlertBox = styled.div<{ tone: Tone }>`
  display: flex;
  align-items: flex-start;
  gap: ${({ theme }) => theme.space[3]};
  padding: ${({ theme }) => `${theme.space[3]} ${theme.space[4]}`};
  border-radius: ${({ theme }) => theme.radii.md};
  border: 1px solid color-mix(in srgb, ${({ tone }) => toneColors(tone)[1]} 35%, transparent);
  border-inline-start: 4px solid ${({ tone }) => toneColors(tone)[1]};
  background: ${({ tone }) => toneColors(tone)[0]};
  color: ${({ theme }) => theme.colors.fg};
  & > svg {
    flex: none;
    margin-block-start: 0.125rem;
    color: ${({ tone }) => toneColors(tone)[1]};
  }
  & > div {
    display: flex;
    flex-direction: column;
    gap: ${({ theme }) => theme.space[1]};
    min-inline-size: 0;
  }
  strong {
    color: ${({ tone }) => toneColors(tone)[1]};
  }
`;

export interface AlertProps extends Omit<HTMLAttributes<HTMLDivElement>, 'title'> {
  tone?: Tone;
  title?: ReactNode;
  action?: ReactNode;
  children?: ReactNode;
}

/**
 * Message with tone colour + icon + text. Danger/warning alerts are announced assertively
 * (role=alert); others politely (role=status).
 */
export function Alert({ tone = 'info', title, action, children, ...rest }: AlertProps) {
  const role = tone === 'danger' || tone === 'warning' ? 'alert' : 'status';
  const Icon = toneIcons[tone];
  return (
    <AlertBox tone={tone} role={role} {...rest}>
      <Icon />
      <div>
        {title && <strong>{title}</strong>}
        {children && <div>{children}</div>}
        {action && <div style={{ marginBlockStart: 8 }}>{action}</div>}
      </div>
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
    font-size: ${({ theme }) => theme.fontSizes.xl};
    line-height: 1.35;
    ${({ theme }) => `@media (min-width: ${theme.breakpoints.md})`} {
      font-size: ${({ theme }) => theme.fontSizes['2xl']};
    }
  }
  p {
    margin: ${({ theme }) => theme.space[1]} 0 0;
    max-inline-size: 48rem;
    color: ${({ theme }) => theme.colors.fgMuted};
  }
`;

const HeaderActions = styled.div`
  display: flex;
  gap: ${({ theme }) => theme.space[2]};
  flex-wrap: wrap;
  align-items: center;
`;

export interface PageHeaderProps {
  title: ReactNode;
  description?: ReactNode;
  /** Primary action goes last (inline end), secondary actions before it. */
  actions?: ReactNode;
  /** Optional back link rendered above the title. */
  before?: ReactNode;
}

export function PageHeader({ title, description, actions, before }: PageHeaderProps) {
  return (
    <HeaderRow>
      <div style={{ minWidth: 0 }}>
        {before && <div style={{ marginBlockEnd: 4 }}>{before}</div>}
        <h1>{title}</h1>
        {description && <p>{description}</p>}
      </div>
      {actions && <HeaderActions>{actions}</HeaderActions>}
    </HeaderRow>
  );
}

const StateBox = styled.div`
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  padding: ${({ theme }) => `${theme.space[10]} ${theme.space[4]}`};
  color: ${({ theme }) => theme.colors.fgMuted};
  & > svg {
    box-sizing: content-box;
    padding: 0.875rem;
    margin-block-end: ${({ theme }) => theme.space[3]};
    border-radius: 9999px;
    background: ${({ theme }) => theme.colors.surfaceMuted};
    box-shadow: ${({ theme }) => theme.shadows.inset};
    color: ${({ theme }) => theme.colors.primary};
  }
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
  /** The next step (e.g. "Add the first student"). */
  action?: ReactNode;
  /** Use "alert" for error states so screen readers announce them. */
  role?: 'alert' | 'status';
  tone?: Tone;
  /** Replace the default icon (an inbox for neutral states, the tone icon otherwise). */
  icon?: ReactNode;
}

/** Empty, forbidden, not-found and error states. */
export function StatePanel({ title, description, action, role, tone = 'neutral', icon }: StatePanelProps) {
  const Icon = tone === 'neutral' ? IconEmpty : toneIcons[tone];
  return (
    <StateBox role={role}>
      {icon ?? <Icon size={28} />}
      <h2>{title}</h2>
      {description && <p>{description}</p>}
      {action && <div style={{ marginBlockStart: 16 }}>{action}</div>}
    </StateBox>
  );
}

export const DescriptionList = styled.dl`
  display: grid;
  grid-template-columns: minmax(8rem, max-content) 1fr;
  gap: 0;
  margin: 0;
  dt,
  dd {
    padding-block: ${({ theme }) => theme.space[2]};
    border-block-end: 1px solid ${({ theme }) => theme.colors.border};
  }
  dt {
    color: ${({ theme }) => theme.colors.fgMuted};
    font-size: ${({ theme }) => theme.fontSizes.sm};
    padding-inline-end: ${({ theme }) => theme.space[6]};
  }
  dd {
    margin: 0;
    font-weight: ${({ theme }) => theme.fontWeights.medium};
    overflow-wrap: anywhere;
  }
  dt:last-of-type,
  dd:last-of-type {
    border-block-end: none;
  }
`;

/** Pulsing placeholder bar for loading states (decorative; pair with a busy/status announcement). */
export const Skeleton = styled.span<{ width?: string; height?: string }>`
  display: block;
  inline-size: ${({ width = '100%' }) => width};
  block-size: ${({ height = '0.875rem' }) => height};
  border-radius: 9999px;
  background: ${({ theme }) => theme.colors.border};
  animation: eemp-shimmer 1.4s ease-in-out infinite;
`;
