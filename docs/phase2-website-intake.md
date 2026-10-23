# Phase 2 — Signed Website Lead Capture Foundation

**Status:** Implemented on `development`. GitHub SQL/API, frontend and Windows IIS checks must pass on the exact code commit. **No live website has been connected and no local SQL migrations have been applied.**

## Purpose and security boundary

Use only a trusted **website backend** to forward validated, consented enquiries to the CRM. The public web form never sees an integration secret. Meta/Facebook and WhatsApp webhooks are not supported by this route.

**Endpoint:** `POST /api/integrations/website/leads` with `Content-Type: application/json`. Include:

- `X-Website-Timestamp`: Unix epoch seconds (UTC), accepted only within ±5 minutes.
- `X-Website-Signature`: `sha256=` followed by a 64-character hexadecimal HMAC-SHA256 digest.
- Body: `eventId` (8–100 ASCII letters, numbers, hyphens or underscores), `name`, `phone`, optional `email`, `companyName`, `requirement`, and `consentAccepted: true`.

Calculate the signature using the secret bytes as the HMAC key and the exact message bytes:

```text
UTF8(decimal Unix timestamp + ".") || EXACT RAW UTF8 JSON REQUEST BODY
```

Re-serialization or whitespace changes modify the signature. Do not embed the secret in browser JavaScript or transmit it as a query string. Use a cryptographically random event ID for each new enquiry; **retries use the same event ID and identical JSON body**, with a new current timestamp/signature if necessary.

Expected responses: **202** accepted for either a new or identical retried submission; **409** reused event ID with different body; **400** invalid payload/consent; **401** missing, expired or invalid signature; **429** excessive requests; **503** disabled or misconfigured intake. The successful response intentionally omits the internal Lead ID.

## Server-only settings

Configure using approved environment variables/secret storage **on the CRM server**, never in source control or public website assets:

```text
WebsiteIntake__Enabled=true
WebsiteIntake__SigningSecret=<random secret of at least 32 UTF-8 bytes>
WebsiteIntake__OwnerUserId=<numeric BIGINT ID of an active designated CRM user>
```

Settings are off by default. The configured owner receives new Leads with `Website` source (ID 2), `New` status and CRM audit history. An inactive owner or inactive Website source stops intake (503). Rotate/limit secrets and enforce TLS with a trusted certificate.

The server includes a 30 requests/minute/IP per-instance rate limiter. Reverse proxies need trusted forwarded-address handling; use shared edge-level throttling in multi-instance environments. The endpoint is deliberately **server-to-server**, not an anonymous browser form.

## Database safety and reliability

Migration: `20261023163000_AddWebsiteLeadSubmission`, a new `WebsiteLeadSubmissions` table with BIGINT auto-increment primary key, Lead FK, SHA256 hashed event ID, SHA256 request payload hash, created timestamp, and a unique event hash constraint.

Each successful first submission creates a Lead/customer code, idempotency receipt and audit event in a single SQL transaction. Concurrent repeated submissions cannot create a second committed Lead for the same event ID. **A distinct event ID represents a separate enquiry**, even if Phone/Email matches; Duplicate Lead Review handles such pairs manually without merging or deleting records.

## Deployment and testing gates

- Validate signature failures, timestamp expiry, missing consent, disabled settings, duplicate event retries and changed-body conflicts.
- Use a real website backend with consent collection, privacy notice and retention policy; never include secrets or customer PII in application logs.
- Protect the source SQL database: earlier local preflight showed five historical EF migrations and 24 Leads. Complete the approved backup → isolated database restore → migration and data-integrity check before touching that database.
- Verify staging IIS/HTTPS, secure secret provision, owner permissions, browser-to-website form validation, retry/error handling, edge rate limits and monitoring before production.
- **Not implemented:** Facebook/Meta or WhatsApp provider callbacks and their signature verification, queue/dead-letter infrastructure, distributed idempotent throttling, notifications and automatic lead routing.

Phase 2 Website Lead Capture must not be marked production accepted until real staging and website-backend integration tests pass.
