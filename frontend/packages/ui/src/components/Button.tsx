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

const palette = (variant: ButtonVariant) =>
  ({
    primary: { bg: 'var(--eemp-color-primary)', fg: 'var(--eemp-color-primary-fg)', border: 'var(--eemp-color-primary)', hover: 'var(--eemp-color-primary-hover)' },
    secondary: { bg: 'var(--eemp-color-surface)', fg: 'var(--eemp-color-fg)', border: 'var(--eemp-color-border-strong)', hover: 'var(--eemp-color-surface-muted)' },
    danger: { bg: 'var(--eemp-color-danger)', fg: '#ffffff', border: 'var(--eemp-color-danger)', hover: 'var(--eemp-color-danger)' },
    ghost: { bg: 'transparent', fg: 'var(--eemp-color-primary)', border: 'transparent', hover: 'var(--eemp-color-primary-soft)' },
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
  padding-inline: ${({ theme, size = 'md' }) => (size === 'sm' ? theme.space[3] : theme.space[4])};
  inline-size: ${({ fullWidth }) => (fullWidth ? '100%' : 'auto')};
  border-radius: ${({ theme }) => theme.radii.md};
  border: 1px solid ${({ variant = 'primary' }) => palette(variant).border};
  background: ${({ variant = 'primary' }) => palette(variant).bg};
  color: ${({ variant = 'primary' }) => palette(variant).fg};
  font: inherit;
  font-size: ${({ theme, size = 'md' }) => (size === 'sm' ? theme.fontSizes.sm : theme.fontSizes.md)};
  font-weight: ${({ theme }) => theme.fontWeights.semibold};
  text-decoration: none;
  cursor: pointer;
  transition: background-color 120ms ease, filter 120ms ease;
  &:hover:not(:disabled) {
    background: ${({ variant = 'primary' }) => palette(variant).hover};
    filter: ${({ variant }) => (variant === 'danger' ? 'brightness(0.92)' : 'none')};
  }
  &:disabled,
  &[aria-disabled='true'] {
    opacity: 0.6;
    cursor: not-allowed;
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
