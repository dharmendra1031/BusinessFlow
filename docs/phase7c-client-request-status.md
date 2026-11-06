# Phase 7C1 — Resmoh IT request status (browser receipt)

**Scope:** Following Phase 7A/7B, this increment adds **read-only status tracking for the latest project enquiry submitted in the same browser**. This is **not** client account login, multi-device access, or a full Client Portal.

## Flow

1. Visitor completes `/start-project/`. The existing website backend securely signs and submits an enquiry to BusinessFlow's Website Lead Intake.
2. **Only when CRM returns 202 Accepted**, the website issues a signed, HttpOnly, SameSite=Strict receipt cookie expiring after 14 days; production requires HTTPS and Secure cookie. The cookie contains an unpredictable browser submission key and signed expiry; no CRM Lead ID, personal data or signing secret.
3. `/client/` shows request status through the public website's same-origin `GET /api/project-request/status`.
4. The website validates expiry and HMAC before making a server-to-server, timestamped HMAC-SHA256 `POST /api/integrations/website/leads/status`.
5. CRM finds only the matching website submission by its existing SHA-256 idempotency hash, joins the Lead/LeadStatus and returns **status and submission timestamp only**. No internal Lead ID, contacts, contract, quotation, finance records, customer contacts or employee data are returned.
6. Invalid/expired cookies fail with HTTP 401; unknown receipts return 404; disabled/invalid CRM configuration returns 503. The request status response is read-only; current persisted domain IDs remain **BIGINT IDENTITY**.

## Access restrictions

- The receipt is a **short-lived bearer proof** for the latest enquiry only. A copied cookie could be used by another person, so **do not add confidential client fields** to the status endpoint.
- The portal does not verify identity or prove contact ownership. Lost receipts are not recoverable and a new request overwrites the prior receipt in that browser.
- Never allow quotation approval, contracts, payments, user accounts, client personal details, client file downloads, password resets or changes based on this receipt.
- The signed CRM endpoint remains enabled only through `WebsiteIntake:Enabled` and a server-side shared secret; public browsers must never see the signing key or call it directly.
- Apply HTTPS, HSTS, reverse proxy rules, WAF limits, and secret rotation before production. The built-in 5/min/IP rate limiter is per app instance.
- The browser-specific receipt is documented on the initial privacy page, but the final legal notice and retention policy still require review.

## Technical verification

- CRM SQL/API integration test signs a status lookup, verifies status-only response, bad signature, expired signature, unknown ID and no extra CRM writes.
- Public website test checks receipt cookie, status lookup, tampering, no-cookie behavior and absence of Lead IDs.
- Windows CI packages `/client/` and `client-status.js` in the separate Resmoh PublicSite IIS artifact.
- Test the **real staging pair** of public site and CRM with matching server secrets and safe synthetic enquiries, including 320px mobile layout, a shared-browser risk review and simulated CRM outage.

## Before full Phase 7C client login

Introduce a **verified client identity** (trusted email ownership proof or an approved administrator invitation), numeric customer/client user ID mapping, separate client authentication sessions, tenant/customer-level authorization and revocation. Only then may the portal expose quotations, contracts, payments or project files, with explicit UAT approval and security tests. Do not reuse Employee/Admin auth permissions.
