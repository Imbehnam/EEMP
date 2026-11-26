'use client';

import isPropValid from '@emotion/is-prop-valid';
import styled from '@emotion/styled';
import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from 'react';

import { Spinner } from './primitives';

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost';
export type ButtonSize = 'sm' | 'md' | 'lg';

interface StyleProps {
  variant?: ButtonVariant;
  size?: ButtonSize;
  fullWidth?: boolean;
}

/**
 * Primary/danger are solid accent fills (never shadow-only); secondary is a raised soft button with
 * a visible boundary; ghost is text-only. All press "into" the surface on :active.
 */
const palette = (variant: ButtonVariant) =>
  ({
    primary: { bg: 'var(--eemp-color-primary)', fg: 'var(--eemp-color-primary-fg)', border: 'var(--eemp-color-primary)', hover: 'var(--eemp-color-primary-hover)', shadow: 'var(--eemp-shadow-raised-sm)' },
    secondary: { bg: 'var(--eemp-color-surface)', fg: 'var(--eemp-color-fg)', border: 'var(--eemp-color-border-strong)', hover: 'var(--eemp-color-surface)', shadow: 'var(--eemp-shadow-raised-sm)' },
    danger: { bg: 'var(--eemp-color-danger)', fg: 'var(--eemp-color-danger-fg)', border: 'var(--eemp-color-danger)', hover: 'var(--eemp-color-danger)', shadow: 'var(--eemp-shadow-raised-sm)' },
    ghost: { bg: 'transparent', fg: 'var(--eemp-color-primary)', border: 'transparent', hover: 'var(--eemp-color-primary-soft)', shadow: 'none' },
  })[variant];

/** Shared look for <button> and link-buttons (`as={Link}`). */
export const ButtonBase = styled('button', {
  shouldForwardProp: (prop) => isPropValid(prop) && prop !== 'variant' && prop !== 'size' && prop !== 'fullWidth',
})<StyleProps>`
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: ${({ theme }) => theme.space[2]};
  min-block-size: ${({ theme, size = 'md' }) => theme.controlHeight[size]};
  min-inline-size: ${({ theme, size = 'md' }) => theme.controlHeight[size]};
  padding-inline: ${({ theme, size = 'md' }) => (size === 'sm' ? theme.space[3] : size === 'lg' ? theme.space[6] : theme.space[5])};
  inline-size: ${({ fullWidth }) => (fullWidth ? '100%' : 'auto')};
  border-radius: ${({ theme }) => theme.radii.md};
  border: 1px solid ${({ variant = 'primary' }) => palette(variant).border};
  background: ${({ variant = 'primary' }) => palette(variant).bg};
  color: ${({ variant = 'primary' }) => palette(variant).fg};
  box-shadow: ${({ variant = 'primary' }) => palette(variant).shadow};
  font: inherit;
  font-size: ${({ theme, size = 'md' }) => (size === 'sm' ? theme.fontSizes.sm : theme.fontSizes.md)};
  font-weight: ${({ theme }) => theme.fontWeights.semibold};
  line-height: 1.2;
  text-decoration: none;
  white-space: nowrap;
  cursor: pointer;
  user-select: none;
  transition:
    background-color ${({ theme }) => theme.motion.fast} ease,
    box-shadow ${({ theme }) => theme.motion.base} ${({ theme }) => theme.motion.ease},
    transform ${({ theme }) => theme.motion.fast} ease,
    filter ${({ theme }) => theme.motion.fast} ease;
  @media (pointer: coarse) {
    min-block-size: ${({ theme }) => theme.controlHeight.md};
  }
  &:hover:not(:disabled):not([aria-disabled='true']) {
    background: ${({ variant = 'primary' }) => palette(variant).hover};
    color: ${({ variant = 'primary' }) => palette(variant).fg};
    filter: ${({ variant }) => (variant === 'danger' ? 'brightness(0.93)' : variant === 'secondary' ? 'brightness(1.02)' : 'none')};
    text-decoration: none;
  }
  &:active:not(:disabled):not([aria-disabled='true']) {
    box-shadow: ${({ variant = 'primary' }) => (variant === 'ghost' ? 'none' : 'var(--eemp-shadow-pressed)')};
    transform: translateY(1px);
  }
  &:disabled:not([aria-busy='true']),
  &[aria-disabled='true'] {
    cursor: not-allowed;
    box-shadow: none;
    background: var(--eemp-color-surface-muted);
    border-color: var(--eemp-color-border);
    border-style: dashed;
    color: var(--eemp-color-fg-muted);
  }
  &[aria-busy='true'] {
    cursor: progress;
    opacity: 0.85;
  }
`;

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement>, StyleProps {
  /** Shows a spinner, sets aria-busy and disables the button. Keep the label visible. */
  loading?: boolean;
  icon?: ReactNode;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { loading = false, disabled, icon, children, type = 'button', ...rest },
  ref,
) {
  return (
    <ButtonBase ref={ref} type={type} disabled={disabled || loading} aria-busy={loading || undefined} {...rest}>
      {loading ? <Spinner /> : icon}
      {children}
    </ButtonBase>
  );
});
