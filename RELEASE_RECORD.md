# Release acceptance record (copy for each candidate)

Status: **Not accepted** until every required check below has dated evidence and an owner.

| Item | Value / evidence link | Owner and date |
| --- | --- | --- |
| Commit SHA and branch | | |
| IIS ZIP SHA-256 (match `release-manifest.json`) | | |
| EF bundle SHA-256 (match `release-manifest.json`) | | |
| Main CI and Windows package CI runs | | |
| Staging HTTPS domain, Windows/.NET/SQL versions | | |
| Existing-schema migration rehearsal and EF history | | |
| Fresh database migration rehearsal | | |
| Backup file, off-server copy and checksum | | |
| Isolated restore, DBCC CHECKDB and recovery time | | |
| Staging role workflows and RDLC PDF preview | | |
| CSRF, cookie, session expiry and permission revocation | | |
| Load, accessibility and non-Chromium checks | | |
| Central log collector and alert receipt | | |
| Previous artifact/config rollback and downtime | | |
| Production smoke check and monitoring owner | | |
| Final approver and approval date | | |

Stop deployment when any required check fails or evidence is missing. Keep credentials,
customer data and backup contents out of this record.
