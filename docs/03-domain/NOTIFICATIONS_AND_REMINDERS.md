# Notifications and Configurable Reminders

## Reminder offsets
Initial defaults requested:
- 1 day before
- 12 hours before
- 3 hours before
- 30 minutes before

Each offset is independently configurable:
- enabled/disabled;
- amount and unit;
- applicable event types;
- recipient groups;
- channel;
- template and locale;
- tenant default and optional branch override;
- quiet hours and timezone policy.

## Precedence proposal
Platform safety limits → tenant policy → branch override (if enabled) → user preference/consent. Exact legal opt-out precedence must be validated per country/channel.

## Scheduling
- Create schedule records when event is created/updated.
- Recalculate pending reminders when time, participant, status, or policy changes.
- Use durable jobs and outbox.
- Deduplicate by event ID + event version + recipient + channel + offset key.
- On reschedule/cancel, supersede pending reminders; retain delivery history.
- Handle daylight-saving transitions using IANA timezone and event-local semantics.
- Define late scheduling behavior when a reminder time is already past.
- Retry transient provider failures with bounded exponential backoff.
- Dead-letter permanent failures and provide operations dashboard.

## Audit
Track scheduled time, actual attempt time, provider, result, retry count, template version, and non-sensitive delivery metadata. Do not log full sensitive message content by default.
