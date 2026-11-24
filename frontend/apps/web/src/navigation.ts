import type { PortalKind } from '@eemp/api-client';
import type { FrameNavSection } from '@eemp/app';

/** Portal navigation. "My students" only for guardians (the API enforces it as well). */
export function portalNavigation(kind: PortalKind | null): FrameNavSection[] {
  return [
    {
      items: [
        { href: '/', labelKey: 'home' },
        { href: '/profile', labelKey: 'profile' },
        ...(kind === 'guardian' ? [{ href: '/students', labelKey: 'myStudents' }] : []),
      ],
    },
    { labelKey: 'accountSection', items: [{ href: '/settings', labelKey: 'settings' }] },
  ];
}
