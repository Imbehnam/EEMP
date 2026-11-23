'use client';

import styled from '@emotion/styled';
import { useEffect, useId, useRef, type ReactNode } from 'react';

import { Button } from './Button';

const Dialog = styled.dialog`
  inline-size: min(28rem, calc(100vw - 2rem));
  border: 1px solid ${({ theme }) => theme.colors.border};
  border-radius: ${({ theme }) => theme.radii.lg};
  background: ${({ theme }) => theme.colors.surface};
  color: ${({ theme }) => theme.colors.fg};
  box-shadow: ${({ theme }) => theme.shadows.overlay};
  padding: ${({ theme }) => theme.space[6]};
  &::backdrop {
    background: rgb(15 20 27 / 0.55);
  }
  h2 {
    margin: 0 0 ${({ theme }) => theme.space[2]};
    font-size: ${({ theme }) => theme.fontSizes.lg};
  }
`;

export interface ConfirmDialogProps {
  open: boolean;
  title: ReactNode;
  description?: ReactNode;
  confirmLabel: string;
  cancelLabel: string;
  tone?: 'danger' | 'primary';
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
  children?: ReactNode;
}

/**
 * Modal confirmation built on the native <dialog> element: focus is moved into the dialog,
 * the rest of the page is inert, Escape cancels, and focus returns to the opener on close.
 */
export function ConfirmDialog({
  open, title, description, confirmLabel, cancelLabel, tone = 'danger', busy, onConfirm, onCancel, children,
}: ConfirmDialogProps) {
  const ref = useRef<HTMLDialogElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const titleId = useId();
  const descId = useId();
  const opener = useRef<Element | null>(null);

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      opener.current = document.activeElement;
      if (typeof dialog.showModal === 'function') dialog.showModal();
      else dialog.setAttribute('open', '');
      cancelRef.current?.focus();
    } else if (!open && dialog.open) {
      if (typeof dialog.close === 'function') dialog.close();
      else dialog.removeAttribute('open');
      (opener.current as HTMLElement | null)?.focus?.();
    }
  }, [open]);

  return (
    <Dialog
      ref={ref}
      aria-labelledby={titleId}
      aria-describedby={description ? descId : undefined}
      onCancel={(e) => {
        e.preventDefault();
        if (!busy) onCancel();
      }}
    >
      <h2 id={titleId}>{title}</h2>
      {description && <p id={descId} style={{ marginBlock: '0 1rem' }}>{description}</p>}
      {children}
      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginBlockStart: 16 }}>
        <Button ref={cancelRef} variant="secondary" onClick={onCancel} disabled={busy}>
          {cancelLabel}
        </Button>
        <Button variant={tone === 'danger' ? 'danger' : 'primary'} onClick={onConfirm} loading={busy}>
          {confirmLabel}
        </Button>
      </div>
    </Dialog>
  );
}
