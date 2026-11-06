# Resmoh IT — Phase 7C2-A Client Authentication

**Scope:** Admin-managed client accounts, one-time email code sign-in, dedicated client cookie, minimal verified client profile and public website login/overview. No quotation, invoice, contract, payment or project-data endpoints are activated.

## Security model
- `ClientPortalAccounts` and `ClientLoginChallenges` are **BIGINT IDENTITY** SQL Server tables. Migration: `20261106150000_AddClientPortalAuthentication`.
- Existing Customer and CustomerContact records are reused. Admin must approve a contact tied to an active, unmerged customer. Duplicate active email access and duplicate contact enrollment are rejected. Customer/Contact ownership is rechecked for every authenticated request.
- Admin endpoints: `GET/POST /api/admin/client-access`, `PATCH /api/admin/client-access/{id}/status`. **Admin staff JWT only.** Revoke increments session version and consumes pending codes.
- Client endpoints: `POST /api/client/auth/request-code`, `POST /api/client/auth/verify-code`, `GET /api/client/me`, `POST /api/client/auth/logout`.
- The 8-digit code is generated with a cryptographic RNG. Only a HMAC-SHA256 hash with an independently configured secret/pepper is persisted. Codes expire in 10 minutes, have at most 5 attempts, and only the latest unused code is accepted. A 60-second resend cooldown and 5 requests/hour/account reduce email abuse. Generic accepted responses prevent revealing unknown accounts.
- Client session uses **ClientPortal** cookie authentication, named `Resmoh.Client`, HttpOnly, Secure in production, SameSite=Strict, cookie Path `/api/client`, 8-hour expiry and no sliding renewal. Every request rechecks active customer/contact, email and session version. Employee `CRM.Session` JWT authentication remains the default; client cookie cannot authenticate internal/admin routes.
- Public website server only proxies allowlisted `/api/client/auth/request-code`, `/api/client/auth/verify-code`, `/api/client/auth/logout` and `/api/client/me` to the CRM backend. All other CRM routes are blocked and cookies other than `Resmoh.Client` are not forwarded. Origin check applies to public POST endpoints. No client OTP secret is included in JavaScript.
- No new client-access capability is granted merely because an enquiry contains an email. Client login is **disabled by default**.
- All customer-facing API responses are explicitly projected. No financial data or Admin DTOs are returned.

## Server configuration (never commit secrets)

CRM private environment:
```
ClientPortal__Enabled=true
ClientPortal__OtpPepper=<random secret of at least 32 UTF-8 bytes>
ClientPortal__Mail__Host=<approved SMTP host>
ClientPortal__Mail__Port=587
ClientPortal__Mail__User=<SMTP login>
ClientPortal__Mail__Password=<SMTP password>
ClientPortal__Mail__From=<verified sender email>
```
The SMTP transport uses STARTTLS/TLS. Require a provider that supports it. The system refuses OTP login when configuration is absent; the OTP is never returned to browser, API or logs. Ensure trusted SMTP credentials, transport and egress settings before enabling.

Public site private environment:
```
PublicSite__Origin=https://resmoh.in
BusinessFlow__ClientApiUrl=https://<approved CRM host>/api/client
```
Use HTTPS, DNS/IIS binding, reverse-proxy trust and WAF/IP protection. Persist and restrict ASP.NET Core Data Protection encryption keys on the CRM host so application restarts don't invalidate clients' cookies unexpectedly; secure and share keys only across authorized CRM instances.

## Operator acceptance
1. Isolate and back up SQL Server before applying `AddClientPortalAuthentication`. Do not change production until staging UAT and rollback verification are approved.
2. Verify Admin enrollment, duplicate contact rejection, revocation and contact/customer disable.
3. Verify OTP delivery from real staging SMTP, wrong/expired/replayed code, cooldown, max attempts, credential masking and no SMTP secret in logs.
4. Verify `/client/login/` and `/client/account/` on staging mobile/desktop, 401, 429, 503, logout and network failure.
5. Security-test client session vs Admin routes; lost/changed contact email; account revoked while cookie exists; malicious origin, cookie forwarding and rate-limits at the edge.
6. Complete final legal privacy notice, email deliverability, alerting, DP key management and release review. **No live `resmoh.in` deployment is included.**

Next phase `7C2-B`: Customer-scoped, read-only sent quotations and issued invoices; only after login staging security acceptance.
