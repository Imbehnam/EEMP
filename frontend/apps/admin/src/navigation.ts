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
      labelKey: 'academicSection',
      items: [
        ...(can('catalog.read')
          ? [
              { href: '/academic/years', labelKey: 'academicYears' },
              { href: '/academic/subjects', labelKey: 'subjects' },
              { href: '/academic/courses', labelKey: 'courses' },
              { href: '/academic/grading-schemes', labelKey: 'gradingSchemes' },
            ]
          : []),
        ...(can('classes.read') ? [{ href: '/academic/classes', labelKey: 'classes' }] : []),
        ...(can('reportcards.read') ? [{ href: '/academic/report-cards', labelKey: 'reportCards' }] : []),
      ],
    },
    {
      labelKey: 'financeSection',
      items: [
        ...(can('finance.read')
          ? [
              { href: '/finance/invoices', labelKey: 'financeInvoices' },
              { href: '/finance/payments', labelKey: 'financePayments' },
              { href: '/finance/approvals', labelKey: 'financeApprovals' },
              { href: '/finance/accounts', labelKey: 'financeAccounts' },
            ]
          : []),
        ...(can('finance.reports.read') ? [{ href: '/finance/reports', labelKey: 'financeReports' }] : []),
        ...(can('finance.read') || can('finance.setup.manage') ? [{ href: '/finance/fees', labelKey: 'financeFees' }] : []),
        ...(can('finance.setup.manage') ? [{ href: '/finance/settings', labelKey: 'financeSettings' }] : []),
      ],
    },
    {
      // Any tenant user can be the approver of a step (permission-based), so the inbox and "my requests" are always offered.
      labelKey: 'workflowSection',
      items: [
        { href: '/workflow/inbox', labelKey: 'workflowInbox' },
        { href: '/workflow/requests', labelKey: 'workflowRequests' },
        ...(can('workflow.definitions.read') ? [{ href: '/workflow/definitions', labelKey: 'workflowDefinitions' }] : []),
      ],
    },
    {
      // EEMP-012: consultant duties come from consultation.sessions.manage (plus a consultant profile, checked by the API).
      labelKey: 'consultationSection',
      items: [
        ...(can('consultation.sessions.manage') || can('consultation.consultants.manage') ? [{ href: '/consultation/schedule', labelKey: 'consultationSchedule' }] : []),
        ...(can('consultation.sessions.manage') || can('consultation.bookings.read') || can('consultation.notes.read')
          ? [{ href: '/consultation/bookings', labelKey: 'consultationBookings' }] : []),
        ...(can('consultation.consultants.manage') || can('consultation.bookings.read') ? [{ href: '/consultation/consultants', labelKey: 'consultants' }] : []),
        ...(can('support.tickets.read') ? [{ href: '/support', labelKey: 'supportInbox' }] : []),
      ],
    },
    {
      labelKey: 'peopleSection',
      items: PERSON_KINDS.filter((kind) => can(`${kind}.read`)).map((kind) => ({ href: `/people/${kind}`, labelKey: kind })),
    },
    account,
  ];
}
