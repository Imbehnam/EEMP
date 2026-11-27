'use client';

import styled from '@emotion/styled';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

import { IconClose, toneIcons } from './icons';
import type { Tone } from './primitives';

export interface ToastOptions {
  tone?: Exclude<Tone, 'neutral'>;
  /** Auto-dismiss delay in ms (paused while hovered or focused). 0 keeps it until closed. */
  duration?: number;
}

interface ToastItem extends Required<ToastOptions> {
  id: number;
  message: ReactNode;
}

interface ToastApi {
  show: (message: ReactNode, options?: ToastOptions) => void;
}

const ToastContext = createContext<ToastApi | null>(null);

/** Non-blocking feedback after an action ("Saved", "Created"). Falls back to a no-op outside the provider. */
export function useToast(): ToastApi {
  return useContext(ToastContext) ?? { show: () => undefined };
}

const Region = styled.div`
  position: fixed;
  z-index: ${({ theme }) => theme.zIndices.toast};
  inset-block-end: ${({ theme }) => theme.space[4]};
  inset-inline-end: ${({ theme }) => theme.space[4]};
  inset-inline-start: ${({ theme }) => theme.space[4]};
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: ${({ theme }) => theme.space[2]};
  pointer-events: none;
  @media (min-width: ${({ theme }) => theme.breakpoints.sm}) {
    inset-inline-start: auto;
  }
`;

const ToastBox = styled.div<{ tone: Tone }>`
  pointer-events: auto;
  display: flex;
  align-items: flex-start;
  gap: ${({ theme }) => theme.space[3]};
  inline-size: min(26rem, 100%);
  padding: ${({ theme }) => `${theme.space[3]} ${theme.space[3]} ${theme.space[3]} ${theme.space[4]}`};
  border-radius: ${({ theme }) => theme.radii.lg};
  border: 1px solid var(--eemp-card-edge, transparent);
  border-inline-start: 4px solid var(--tone);
  background: ${({ theme }) => theme.colors.surface};
  box-shadow: ${({ theme }) => theme.shadows.raisedLg};
  color: ${({ theme }) => theme.colors.fg};
  --tone: ${({ tone }) => `var(--eemp-color-${tone === 'neutral' ? 'fg' : tone})`};
  animation: eemp-toast-in 200ms ease-out;
  & > svg {
    flex: none;
    margin-block-start: 0.125rem;
    color: var(--tone);
  }
  & > p {
    flex: 1;
    margin: 0;
    padding-block-start: 0.0625rem;
  }
  @keyframes eemp-toast-in {
    from {
      opacity: 0;
      transform: translateY(8px);
    }
  }
`;

const Close = styled.button`
  flex: none;
  display: inline-grid;
  place-items: center;
  inline-size: ${({ theme }) => theme.controlHeight.sm};
  block-size: ${({ theme }) => theme.controlHeight.sm};
  margin: -0.25rem 0;
  border: 0;
  border-radius: 9999px;
  background: transparent;
  color: ${({ theme }) => theme.colors.fgMuted};
  cursor: pointer;
  &:hover {
    background: ${({ theme }) => theme.colors.surfaceMuted};
    color: ${({ theme }) => theme.colors.fg};
  }
  @media (pointer: coarse) {
    inline-size: ${({ theme }) => theme.controlHeight.md};
    block-size: ${({ theme }) => theme.controlHeight.md};
  }
`;

function ToastView({ item, closeLabel, onClose }: { item: ToastItem; closeLabel: string; onClose: () => void }) {
  const [paused, setPaused] = useState(false);
  const close = useRef(onClose);
  close.current = onClose;
  useEffect(() => {
    if (!item.duration || paused) return;
    const handle = setTimeout(() => close.current(), item.duration);
    return () => clearTimeout(handle);
  }, [item.duration, paused]);
  const Icon = toneIcons[item.tone];
  return (
    <ToastBox
      tone={item.tone}
      role={item.tone === 'danger' ? 'alert' : 'status'}
      onMouseEnter={() => setPaused(true)}
      onMouseLeave={() => setPaused(false)}
      onFocus={() => setPaused(true)}
      onBlur={() => setPaused(false)}
    >
      <Icon />
      <p>{item.message}</p>
      <Close type="button" aria-label={closeLabel} onClick={onClose}>
        <IconClose size={16} />
      </Close>
    </ToastBox>
  );
}

export interface ToastProviderProps {
  /** Translated label for the close button (e.g. t('common.close')). */
  closeLabel: string;
  children: ReactNode;
}

export function ToastProvider({ closeLabel, children }: ToastProviderProps) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const next = useRef(1);
  const dismiss = useCallback((id: number) => setItems((all) => all.filter((t) => t.id !== id)), []);
  const api = useMemo<ToastApi>(
    () => ({
      show: (message, options) =>
        setItems((all) => [
          ...all.slice(-2),
          { id: next.current++, message, tone: options?.tone ?? 'success', duration: options?.duration ?? 7000 },
        ]),
    }),
    [],
  );
  return (
    <ToastContext.Provider value={api}>
      {children}
      <Region>
        {items.map((item) => (
          <ToastView key={item.id} item={item} closeLabel={closeLabel} onClose={() => dismiss(item.id)} />
        ))}
      </Region>
    </ToastContext.Provider>
  );
}
