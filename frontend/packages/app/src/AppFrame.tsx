'use client';

import { Link, usePathname } from '@eemp/i18n/navigation';
import { AppShell, type Crumb, type LinkComponent, type NavSection } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import type { ReactNode } from 'react';

import type { ThemePreference } from './cookies';
import { isActivePath } from './paths';
import { LocaleSwitcher } from './LocaleSwitcher';
import { useSession } from './session';
import { SignOutButton } from './SignOutButton';
import { ThemeToggle } from './ThemeToggle';

export interface FrameNavItem {
  href: string;
  /** Key in the `nav` namespace. */
  labelKey: string;
}

export interface FrameNavSection {
  /** Key in the `nav` namespace. */
  labelKey?: string;
  items: FrameNavItem[];
}

/** Authenticated chrome: navigation (pre-filtered by permission), language, theme, sign-out. */
export function AppFrame({
  brand, context, sections, theme, children,
}: { brand: string; context?: ReactNode; sections: FrameNavSection[]; theme: ThemePreference; children: ReactNode }) {
  const t = useTranslations('nav');
  const common = useTranslations('common');
  const shell = useTranslations('shell');
  const pathname = usePathname();
  const { user } = useSession();
  // Longest matching href wins so "/people/students/new" highlights "Students" only.
  const activeHref = sections
    .flatMap((s) => s.items)
    .filter((i) => isActivePath(pathname, i.href))
    .sort((a, b) => b.href.length - a.href.length)[0]?.href;

  // Breadcrumb trail derived from the navigation: Home › Section › Page (linked while on a sub-page).
  const homeHref = sections[0]?.items[0]?.href ?? '/';
  const activeSection = sections.find((s) => s.items.some((i) => i.href === activeHref));
  const activeItem = activeSection?.items.find((i) => i.href === activeHref);
  const breadcrumbs: Crumb[] = [];
  if (activeItem && activeItem.href !== homeHref) {
    breadcrumbs.push({ label: t(sections[0]?.items[0]?.labelKey ?? 'home'), href: homeHref });
    if (activeSection?.labelKey) breadcrumbs.push({ label: t(activeSection.labelKey) });
    const onItemPage = pathname.replace(/\/+$/, '') === activeItem.href;
    breadcrumbs.push({ label: t(activeItem.labelKey), href: activeItem.href, current: onItemPage });
  }

  const navSections: NavSection[] = sections
    .filter((s) => s.items.length > 0)
    .map((s) => ({
      label: s.labelKey ? t(s.labelKey) : undefined,
      items: s.items.map((i) => ({ href: i.href, label: t(i.labelKey), active: i.href === activeHref })),
    }));

  return (
    <AppShell
      brand={brand}
      context={context}
      sections={navSections}
      breadcrumbs={breadcrumbs}
      Link={Link as unknown as LinkComponent}
      labels={{
        skipToContent: common('skipToContent'),
        primaryNav: common('primaryNav'),
        openMenu: common('openMenu'),
        closeMenu: common('closeMenu'),
        breadcrumb: common('breadcrumb'),
      }}
      headerActions={
        <>
          <span className="eemp-hide-sm eemp-compact-label" data-testid="signed-in-user">
            {shell('signedInAs', { name: user.displayName })}
          </span>
          <LocaleSwitcher />
          <ThemeToggle initial={theme} />
          <SignOutButton />
        </>
      }
    >
      {children}
    </AppShell>
  );
}
