import type { CurrentUser } from '@eemp/api-client';
import { PERSON_KINDS } from '@eemp/api-client';
import type { FrameNavSection } from '@eemp/app';

/** Navigation filtered by the user's permissions (UX only; the API enforces authorization). */
export function adminNavigation(user: CurrentUser): FrameNavSection[] {
  const can = (p: string) => user.permissions.includes(p);
  const account: FrameNavSection = { labelKey: 'accountSection', items: [{ href: '/settings', labelKey: 'settings' }] };
  if (user.isPlatformAdmin) {
    return [
      { items: [{ href: '/', labelKey: 'dashboard' }] },
      { labelKey: 'platformSection', items: [{ href: '/tenants', labelKey: 'tenants' }] },
      account,
    ];
  }
  return [
    { items: [{ href: '/', labelKey: 'dashboard' }] },
    {
      labelKey: 'organizationSection',
      items: [
        ...(can('organization.read') ? [{ href: '/organization', labelKey: 'organization' }] : []),
        ...(can('branches.read') ? [{ href: '/branches', labelKey: 'branches' }] : []),
      ],
    },
    {
      labelKey: 'accessSection',
      items: [
        ...(can('roles.read') ? [{ href: '/roles', labelKey: 'roles' }] : []),
        ...(can('users.read') ? [{ href: '/users', labelKey: 'users' }] : []),
      ],
    },
    {
      labelKey: 'peopleSection',
      items: PERSON_KINDS.filter((kind) => can(`${kind}.read`)).map((kind) => ({ href: `/people/${kind}`, labelKey: kind })),
    },
    account,
  ];
}
