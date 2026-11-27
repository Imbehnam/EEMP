'use client';

import styled from '@emotion/styled';
import { useEffect, useId, useRef, useState, type ComponentType, type ReactNode } from 'react';

import { mq } from '../tokens';
import { IconChevron } from './direction';
import { IconClose, IconMenu } from './icons';
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

export interface Crumb {
  label: string;
  /** Omit for the current page or a non-navigable group. */
  href?: string;
  current?: boolean;
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
  /** aria-label of the breadcrumb <nav>. */
  breadcrumb?: string;
}

export interface AppShellProps {
  brand: ReactNode;
  /** Context line under the brand: tenant/branch or "Platform operations". */
  context?: ReactNode;
  sections: NavSection[];
  headerActions?: ReactNode;
  /** Trail shown above every page (Home › Section › Page). Hidden when it has fewer than 2 entries. */
  breadcrumbs?: Crumb[];
  labels: AppShellLabels;
  Link: LinkComponent;
  children: ReactNode;
}

const HEADER_HEIGHT = '4rem';

const Layout = styled.div`
  min-block-size: 100vh;
  display: grid;
  grid-template-columns: 1fr;
  grid-template-rows: auto 1fr;
  ${mq('lg')} {
    grid-template-columns: 17rem minmax(0, 1fr);
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
  min-block-size: ${HEADER_HEIGHT};
  padding: ${({ theme }) => `${theme.space[2]} ${theme.space[4]}`};
  background: ${({ theme }) => theme.colors.bg};
  box-shadow: 0 1px 0 ${({ theme }) => theme.colors.border}, 0 6px 16px -12px var(--eemp-shadow-dark);
  grid-column: 1 / -1;
  ${mq('md')} {
    padding-inline: ${({ theme }) => theme.space[6]};
  }
`;

/* Below md the header keeps only menu + brand; language/theme/sign-out move into the drawer. */
const HeaderActions = styled.div`
  display: none;
  ${mq('md')} {
    display: flex;
    flex: none;
    align-items: center;
    gap: ${({ theme }) => theme.space[2]};
  }
`;

const DrawerActions = styled.div`
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: ${({ theme }) => theme.space[3]};
  padding: ${({ theme }) => `${theme.space[3]} ${theme.space[3]} ${theme.space[4]}`};
  margin-block-end: ${({ theme }) => theme.space[2]};
  border-block-end: 1px solid ${({ theme }) => theme.colors.border};
  ${mq('md')} {
    display: none;
  }
`;

const BrandMark = styled.span`
  display: none;
  ${mq('sm')} {
    display: inline-grid;
  }
  place-items: center;
  flex: none;
  inline-size: 2.5rem;
  block-size: 2.5rem;
  border-radius: ${({ theme }) => theme.radii.md};
  background: ${({ theme }) => theme.colors.primary};
  color: ${({ theme }) => theme.colors.primaryFg};
  box-shadow: ${({ theme }) => theme.shadows.raisedSm};
  font-weight: 800;
  font-size: 1.125rem;
`;

const Backdrop = styled.div`
  position: fixed;
  inset: 0;
  z-index: ${({ theme }) => theme.zIndices.drawer};
  background: ${({ theme }) => theme.colors.backdrop};
  ${mq('lg')} {
    display: none;
  }
`;

const Sidebar = styled.aside<{ open: boolean }>`
  background: ${({ theme }) => theme.colors.bg};
  padding: ${({ theme }) => `${theme.space[4]} ${theme.space[3]}`};
  /* Mobile/tablet: off-canvas drawer above a backdrop. */
  display: ${({ open }) => (open ? 'block' : 'none')};
  position: fixed;
  z-index: ${({ theme }) => theme.zIndices.drawer + 1};
  inset-block: 0;
  inset-inline-start: 0;
  inline-size: min(20rem, 86vw);
  overflow-y: auto;
  overscroll-behavior: contain;
  box-shadow: ${({ theme }) => theme.shadows.raisedLg};
  border-start-end-radius: ${({ theme }) => theme.radii.lg};
  border-end-end-radius: ${({ theme }) => theme.radii.lg};
  animation: eemp-drawer-in 200ms ease-out;
  @keyframes eemp-drawer-in {
    from {
      opacity: 0;
      transform: translateX(calc(var(--eemp-lx) * -1.5rem));
    }
  }
  ${mq('lg')} {
    display: block;
    position: sticky;
    z-index: auto;
    inset-block-start: ${HEADER_HEIGHT};
    inline-size: auto;
    block-size: calc(100vh - ${HEADER_HEIGHT});
    box-shadow: none;
    border-radius: 0;
    animation: none;
    padding: ${({ theme }) => `${theme.space[6]} ${theme.space[4]}`};
  }
`;

const DrawerHead = styled.div`
  display: flex;
  justify-content: flex-end;
  margin-block-end: ${({ theme }) => theme.space[2]};
  ${mq('lg')} {
    display: none;
  }
`;

const SectionLabel = styled.p`
  margin: ${({ theme }) => `${theme.space[5]} 0 ${theme.space[2]}`};
  padding-inline: ${({ theme }) => theme.space[3]};
  font-size: ${({ theme }) => theme.fontSizes.xs};
  font-weight: ${({ theme }) => theme.fontWeights.bold};
  letter-spacing: 0.04em;
  color: ${({ theme }) => theme.colors.fgMuted};
`;

const NavList = styled.ul`
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: ${({ theme }) => theme.space[1]};
  a {
    display: flex;
    align-items: center;
    min-block-size: ${({ theme }) => theme.controlHeight.md};
    padding: ${({ theme }) => `${theme.space[2]} ${theme.space[3]}`};
    border-radius: ${({ theme }) => theme.radii.md};
    color: ${({ theme }) => theme.colors.fg};
    text-decoration: none;
    border-inline-start: 3px solid transparent;
    transition: background-color 120ms ease, box-shadow 180ms ease;
  }
  a:hover {
    background: ${({ theme }) => theme.colors.surface};
    box-shadow: ${({ theme }) => theme.shadows.raisedSm};
  }
  /* Selected = pressed in (inset) + accent bar + bold accent text: three cues, not shadow alone. */
  a[aria-current='page'] {
    background: ${({ theme }) => theme.colors.surfaceMuted};
    box-shadow: ${({ theme }) => theme.shadows.inset};
    color: ${({ theme }) => theme.colors.primary};
    border-inline-start-color: ${({ theme }) => theme.colors.primary};
    font-weight: ${({ theme }) => theme.fontWeights.bold};
  }
`;

const Main = styled.main`
  padding: ${({ theme }) => `${theme.space[5]} ${theme.space[4]} ${theme.space[12]}`};
  min-inline-size: 0;
  ${mq('md')} {
    padding: ${({ theme }) => `${theme.space[8]} ${theme.space[8]} ${theme.space[12]}`};
  }
  &:focus {
    outline: none;
  }
`;

const Inner = styled.div`
  max-inline-size: 90rem;
  margin-inline: auto;
`;

const IconButton = styled.button`
  display: inline-grid;
  place-items: center;
  flex: none;
  inline-size: ${({ theme }) => theme.controlHeight.md};
  block-size: ${({ theme }) => theme.controlHeight.md};
  border: 1px solid ${({ theme }) => theme.colors.borderStrong};
  border-radius: ${({ theme }) => theme.radii.md};
  background: ${({ theme }) => theme.colors.surface};
  box-shadow: ${({ theme }) => theme.shadows.raisedSm};
  color: inherit;
  cursor: pointer;
  &:active {
    box-shadow: ${({ theme }) => theme.shadows.pressed};
  }
`;

const MenuButton = styled(IconButton)`
  ${mq('lg')} {
    display: none;
  }
`;

const CrumbList = styled.ol`
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: ${({ theme }) => theme.space[1]};
  list-style: none;
  margin: 0 0 ${({ theme }) => theme.space[3]};
  padding: 0;
  font-size: ${({ theme }) => theme.fontSizes.sm};
  color: ${({ theme }) => theme.colors.fgMuted};
  li {
    display: inline-flex;
    align-items: center;
    gap: ${({ theme }) => theme.space[1]};
  }
  a {
    color: ${({ theme }) => theme.colors.fgMuted};
    text-decoration: underline;
    text-decoration-color: transparent;
  }
  a:hover {
    color: ${({ theme }) => theme.colors.primary};
    text-decoration-color: currentColor;
  }
  [aria-current='page'] {
    color: ${({ theme }) => theme.colors.fg};
    font-weight: ${({ theme }) => theme.fontWeights.semibold};
  }
`;

function Breadcrumbs({ items, label, Link }: { items: Crumb[]; label?: string; Link: LinkComponent }) {
  return (
    <nav aria-label={label}>
      <CrumbList>
        {items.map((c, i) => (
          <li key={`${c.label}-${i}`}>
            {i > 0 && <IconChevron size={14} />}
            {c.href && !c.current ? (
              <Link href={c.href}>{c.label}</Link>
            ) : (
              <span aria-current={c.current ? 'page' : undefined}>{c.label}</span>
            )}
          </li>
        ))}
      </CrumbList>
    </nav>
  );
}

export function AppShell({ brand, context, sections, headerActions, breadcrumbs, labels, Link, children }: AppShellProps) {
  const [open, setOpen] = useState(false);
  const navId = useId();
  const menuButton = useRef<HTMLButtonElement>(null);
  const drawer = useRef<HTMLElement>(null);
  const activeHref = sections.flatMap((s) => s.items).find((i) => i.active)?.href;

  // Close the drawer after navigation.
  useEffect(() => setOpen(false), [activeHref]);

  // Drawer: Escape closes and returns focus; focus moves into the drawer when it opens.
  useEffect(() => {
    if (!open) return;
    drawer.current?.querySelector<HTMLElement>('a, button')?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setOpen(false);
        menuButton.current?.focus();
      }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [open]);

  const brandText = typeof brand === 'string' ? brand : '';

  return (
    <>
      <SkipLink href="#main-content">{labels.skipToContent}</SkipLink>
      <Layout>
        <Header>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, minWidth: 0, flex: '1 1 auto' }}>
            <MenuButton
              ref={menuButton}
              type="button"
              aria-expanded={open}
              aria-controls={navId}
              aria-label={open ? labels.closeMenu : labels.openMenu}
              onClick={() => setOpen((v) => !v)}
            >
              <IconMenu />
            </MenuButton>
            <BrandMark aria-hidden="true">{brandText.trim().charAt(0) || 'E'}</BrandMark>
            <div style={{ minWidth: 0 }}>
              <div style={{ fontWeight: 700, lineHeight: 1.3, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{brand}</div>
              {context && (
                <div style={{ fontSize: '0.8125rem', color: 'var(--eemp-color-fg-muted)', lineHeight: 1.3, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {context}
                </div>
              )}
            </div>
          </div>
          {headerActions && <HeaderActions>{headerActions}</HeaderActions>}
        </Header>
        {open && <Backdrop aria-hidden="true" onClick={() => setOpen(false)} />}
        <Sidebar open={open} id={navId} ref={drawer}>
          <DrawerHead>
            <IconButton type="button" aria-label={labels.closeMenu} onClick={() => { setOpen(false); menuButton.current?.focus(); }}>
              <IconClose />
            </IconButton>
          </DrawerHead>
          {open && headerActions && <DrawerActions>{headerActions}</DrawerActions>}
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
          <Inner>
            {breadcrumbs && breadcrumbs.length > 1 && <Breadcrumbs items={breadcrumbs} label={labels.breadcrumb} Link={Link} />}
            {children}
          </Inner>
        </Main>
      </Layout>
    </>
  );
}
