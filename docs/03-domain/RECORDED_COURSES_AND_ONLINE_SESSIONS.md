# Recorded Courses and Online Sessions

## Recorded course model
Course, module, lesson, media asset, captions, attachments, enrollment, entitlement, access window, progress, completion, and certificate (later phase).

## Media security
Private object storage, signed short-lived URLs or streaming token, optional DRM based on business risk, upload scanning, transcoding pipeline, bandwidth/storage quotas, and no public permanent media links.

## Online meetings
Use provider adapter for meeting creation, update, cancellation, attendance/webhook, and meeting link retrieval. Avoid storing provider secrets in tenant-visible settings. Meeting link access is participant-scoped and time-bound.

## Calendar
Unified calendar aggregates class sessions, consultations, planning sessions, and online meetings. Each event retains source aggregate ID, timezone, recurrence rule, participants, status, and version.
