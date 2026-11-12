# Backup, Recovery, and Data Lifecycle

Define RPO/RTO by service tier. Back up database, object metadata/media, configuration, and critical keys according to recoverability design. Encrypt backups and restrict access. Test restore regularly.

Tenant lifecycle: provision → active → past-due/grace → restricted/suspended → export window → offboarded/deleted according to contract and law. Subscription suspension must not accidentally destroy records. Define read-only/export access and deletion schedule explicitly.
