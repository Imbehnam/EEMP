# Portals and Navigation

## Shared application, role-aware experience
- Public/marketing and tenant sign-up.
- Platform operations console.
- Tenant central-office console.
- Branch management console.
- Teacher workspace.
- Student portal.
- Parent/guardian portal.
- Counselor/planner workspace.

A single Next.js codebase may host these route groups initially. Route visibility is usability only; API authorization remains authoritative.

## UX requirements
- Clear active tenant/branch context.
- Show current role and scope.
- Prevent accidental cross-branch actions.
- Provide accessible responsive layouts.
- Support RTL/LTR and locale switching.
- Provide empty, loading, error, unauthorized, and conflict states.
- Confirm irreversible financial, academic publication, and transfer actions.
- Provide audit/history views to authorized roles.
