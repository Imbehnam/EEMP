import type { PortalKind } from '@eemp/api-client';
import type { FrameNavSection } from '@eemp/app';

/**
 * Portal navigation by linked record (UX only; the API enforces every relationship): teachers get their classes,
 * students and guardians the published academic views, guardians also "My students".
 */
export function portalNavigation(kind: PortalKind | null): FrameNavSection[] {
  return [
    {
      items: [
        { href: '/', labelKey: 'home' },
        { href: '/profile', labelKey: 'profile' },
        ...(kind === 'guardian' ? [{ href: '/students', labelKey: 'myStudents' }] : []),
      ],
    },
    {
      labelKey: 'academicSection',
      items: [
        ...(kind === 'teacher' ? [{ href: '/classes', labelKey: 'myClasses' }] : []),
        ...(kind === 'student' || kind === 'guardian'
          ? [
              { href: '/timetable', labelKey: 'timetable' },
              { href: '/attendance', labelKey: 'attendance' },
              { href: '/grades', labelKey: 'grades' },
              { href: '/report-cards', labelKey: 'reportCards' },
            ]
          : []),
      ],
    },
    ...(kind === 'student' || kind === 'guardian' ? [{ labelKey: 'financeSection', items: [{ href: '/invoices', labelKey: 'invoices' }] }] : []),
    { labelKey: 'accountSection', items: [{ href: '/settings', labelKey: 'settings' }] },
  ];
}
