# Core User Journeys

## Tenant onboarding
1. Platform operator creates tenant and selects plan/trial.
2. Tenant owner accepts terms and configures locale, timezone, currency, branding.
3. Owner creates central office and branches.
4. Owner invites administrators and assigns scoped permissions.
5. Tenant configures academic calendar, fee catalog, workflows, SMS and payment providers.
6. Tenant imports initial data through validated import process.

## Student enrollment
1. Parent/student submits enrollment application.
2. Workflow validates required fields and documents.
3. Registrar reviews and approves/rejects/requests correction.
4. Student identity and guardian relationship are established.
5. Invoice is issued according to approved fee policy.
6. Payment is verified; enrollment becomes active according to policy.
7. Student is assigned to class/term and receives access/card.

## Grade publication
1. Teacher enters assessment scores for assigned class.
2. Validation checks grading scheme, ranges, missing values, and deadline.
3. Authorized reviewer approves.
4. Report card snapshot is generated and versioned.
5. Publication event triggers portal availability and notification.
6. Corrections create a new version with reason and audit.

## Consultation booking
1. Counselor publishes availability and service rules.
2. Student/guardian selects eligible slot.
3. Server atomically reserves slot and creates booking.
4. Payment is collected if required.
5. Meeting link is generated through provider adapter.
6. Configured reminders are scheduled.
7. Reschedule/cancel updates calendar and cancels or supersedes pending reminders.

## Academic planning request
1. Student/guardian submits planning request.
2. Workflow checks eligibility and required data.
3. Fee invoice/payment is created when policy requires.
4. Planner is assigned.
5. Sessions and plan milestones are created.
6. Plan is reviewed, approved, published, and tracked.

## Cross-institute referral
1. Source institute selects eligible record and purpose.
2. Consent/authority and destination are verified.
3. Source selects permitted data categories/fields.
4. Secure referral package is created with expiry.
5. Destination accepts or rejects.
6. Accepted data is imported with provenance and duplicate review.
7. Access grant expires or is revoked; audit remains.

## Payment
1. System creates invoice and payment intent.
2. User is redirected or shown provider checkout.
3. Provider sends callback/webhook.
4. Backend verifies signature and/or queries provider.
5. Idempotent transaction records settlement.
6. Invoice balance and enrollment/service state are updated transactionally.
7. Receipt is generated and notification dispatched.
