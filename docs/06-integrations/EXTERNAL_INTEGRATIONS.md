# External Integrations

## Payment
Provider adapter contract: create payment, verify callback, query transaction, refund, reconcile, and health check. Support multiple gateways by country/currency. Credentials encrypted and never returned to browser.

## SMS
Adapter: send, query delivery, validate sender/configuration, handle provider callback. Provider selection may be platform-managed or tenant-configured within approved options. Consent and opt-out requirements are jurisdiction-specific.

## Video meeting
Adapter: create/update/cancel meeting, fetch participant/join details, webhook validation, attendance sync. Restrict join link to authorized participants and time windows.

## Email/push
Template-based adapters with locale fallback, retry, bounce/failure handling, and preference management.

## Storage/media
Private object storage, multipart upload, signed access, virus scanning, lifecycle/retention, and transcoding.

## Calendar
iCalendar export/import initially; OAuth-based calendar sync later if justified.

## Integration principles
Timeouts, retries with jitter, circuit breakers, idempotency, provider health dashboards, secret rotation, webhook signature validation, and no synchronous dependency for noncritical notifications.
