'use client';

import { Link } from '@eemp/i18n/navigation';
import { ButtonBase, type ButtonSize, type ButtonVariant } from '@eemp/ui';
import type { ReactNode } from 'react';

const LinkBase = ButtonBase.withComponent(Link);

/** A navigation link styled as a button (locale-aware href, e.g. "/users/new"). */
export function ButtonLink({
  href, children, variant = 'primary', size = 'md',
}: { href: string; children: ReactNode; variant?: ButtonVariant; size?: ButtonSize }) {
  return (
    <LinkBase href={href} variant={variant} size={size} style={{ textDecoration: 'none' }}>
      {children}
    </LinkBase>
  );
}
