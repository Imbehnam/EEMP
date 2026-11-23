'use client';

import styled from '@emotion/styled';
import { useEffect, useId, useState, type ComponentType, type ReactNode } from 'react';

import { mq } from '../tokens';
import { SkipLink } from './primitives';

export interface NavItem {
  href: string;
  label: string;
  active?: boolean;
}

export interface NavSection {
  label?: string;
  items: NavItem[];
}

export type LinkComponent = ComponentType<{
  href: string;
  className?: string;
  children: ReactNode;
  'aria-current'?: 'page' | undefined;
  onClick?: () => void;
}>;

export interface AppShellLabels {
  skipToContent: string;
  primaryNav: string;
  openMenu: string;
  closeMenu: string;
}

export interface AppShellProps {
  brand: ReactNode;
  /** Context line under the brand: tenant/branch or "Platform operations". */
  context?: ReactNode;
  sections: NavSection[];
  headerActions?: ReactNode;
  labels: AppShellLabels;
  Link: LinkComponent;
  children: ReactNode;
}

const Layout = styled.div`
  min-block-size: 100vh;
  display: grid;
  grid-template-columns: 1fr;
  ${mq('lg')} {
    grid-template-columns: 16rem 1fr;
  }
`;

const Header = styled.header`
  position: sticky;
  inset-block-start: 0;
  z-index: ${({ theme }) => theme.zIndices.header};
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: ${({ theme }) => theme.space[3]};
  padding: ${({ theme }) => `${theme.space[2]} ${theme.space[4]}`};
  background: ${({ theme }) => theme.colors.surface};
  border-block-end: 1px solid ${({ theme }) => theme.colors.border};
  grid-column: 1 / -1;
`;

const Sidebar = styled.aside<{ open: boolean }>`
  background: ${({ theme }) => theme.colors.surface};
  border-inline-end: 1px solid ${({ theme }) => theme.colors.border};
  padding: ${({ theme }) => theme.space[4]};
  display: ${({ open }) => (open ? 'block' : 'none')};
  ${mq('lg')} {
    display: block;
    position: sticky;
    inset-block-start: 3.5rem;
    block-size: calc(100vh - 3.5rem);
    overflow-y: auto;
  }
`;

const SectionLabel = styled.p`
  margin: ${({ theme }) => `${theme.space[4]} 0 ${theme.space[1]}`};
  padding-inline: ${({ theme }) => theme.space[3]};
  font-size: ${({ theme }) => theme.fontSizes.xs};
  font-weight: ${({ theme }) => theme.fontWeights.bold};
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: ${({ theme }) => theme.colors.fgMuted};
`;

const NavList = styled.ul`
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
  a {
    display: block;
    padding: ${({ theme }) => `${theme.space[2]} ${theme.space[3]}`};
    border-radius: ${({ theme }) => theme.radii.md};
    color: ${({ theme }) => theme.colors.fg};
    text-decoration: none;
    border-inline-start: 3px solid transparent;
  }
  a:hover {
    background: ${({ theme }) => theme.colors.surfaceMuted};
  }
  a[aria-current='page'] {
    background: ${({ theme }) => theme.colors.primarySoft};
    color: ${({ theme }) => theme.colors.primary};
    border-inline-start-color: ${({ theme }) => theme.colors.primary};
    font-weight: ${({ theme }) => theme.fontWeights.semibold};
  }
`;

const Main = styled.main`
  padding: ${({ theme }) => theme.space[4]};
  min-inline-size: 0;
  ${mq('md')} {
    padding: ${({ theme }) => theme.space[8]};
  }
  &:focus {
    outline: none;
  }
`;

const MenuButton = styled.button`
  display: inline-flex;
  align-items: center;
  justify-content: center;
  inline-size: 2.5rem;
  block-size: 2.5rem;
  border: 1px solid ${({ theme }) => theme.colors.border};
  border-radius: ${({ theme }) => theme.radii.md};
  background: transparent;
  color: inherit;
  cursor: pointer;
  ${mq('lg')} {
    display: none;
  }
`;

export function AppShell({ brand, context, sections, headerActions, labels, Link, children }: AppShellProps) {
  const [open, setOpen] = useState(false);
  const navId = useId();
  const activeHref = sections.flatMap((s) => s.items).find((i) => i.active)?.href;

  // Close the mobile drawer after navigation.
  useEffect(() => setOpen(false), [activeHref]);

  return (
    <>
      <SkipLink href="#main-content">{labels.skipToContent}</SkipLink>
      <Layout>
        <Header>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, minWidth: 0 }}>
            <MenuButton
              type="button"
              aria-expanded={open}
              aria-controls={navId}
              aria-label={open ? labels.closeMenu : labels.openMenu}
              onClick={() => setOpen((v) => !v)}
            >
              <svg width="20" height="20" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                <path d="M3 6h18M3 12h18M3 18h18" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
              </svg>
            </MenuButton>
            <div style={{ minWidth: 0 }}>
              <div style={{ fontWeight: 700 }}>{brand}</div>
              {context && <div style={{ fontSize: '0.8125rem', color: 'var(--eemp-color-fg-muted)' }}>{context}</div>}
            </div>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', justifyContent: 'flex-end' }}>
            {headerActions}
          </div>
        </Header>
        <Sidebar open={open} id={navId}>
          <nav aria-label={labels.primaryNav}>
            {sections.map((section, i) => (
              <div key={section.label ?? i}>
                {section.label && <SectionLabel>{section.label}</SectionLabel>}
                <NavList>
                  {section.items.map((item) => (
                    <li key={item.href}>
                      <Link href={item.href} aria-current={item.active ? 'page' : undefined}>
                        {item.label}
                      </Link>
                    </li>
                  ))}
                </NavList>
              </div>
            ))}
          </nav>
        </Sidebar>
        <Main id="main-content" tabIndex={-1}>
          {children}
        </Main>
      </Layout>
    </>
  );
}
