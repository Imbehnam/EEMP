#!/usr/bin/env bash
# Fails when any module DbContext model has changes not captured in a migration.
set -euo pipefail
cd "$(dirname "$0")"
dotnet tool restore >/dev/null
check() {
  dotnet dotnet-ef migrations has-pending-model-changes --no-build --configuration "${CONFIGURATION:-Debug}" --project "$1" --startup-project "$1" --context "$2"
}
check src/EEMP.Infrastructure AuditDbContext
check src/Modules/Tenancy/EEMP.Modules.Tenancy.Infrastructure TenancyDbContext
check src/Modules/Identity/EEMP.Modules.Identity.Infrastructure IdentityModuleDbContext
check src/Modules/Organization/EEMP.Modules.Organization.Infrastructure OrganizationDbContext
check src/Modules/People/EEMP.Modules.People.Infrastructure PeopleDbContext
check src/Modules/Academic/EEMP.Modules.Academic.Infrastructure AcademicDbContext
check src/Modules/Finance/EEMP.Modules.Finance.Infrastructure FinanceDbContext
check src/Modules/Workflow/EEMP.Modules.Workflow.Infrastructure WorkflowDbContext
