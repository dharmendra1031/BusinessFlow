# Phase 2 — Website Lead Capture Foundation

**Status:** Website API implementation; verify GitHub CI for the exact commit before accepting. **No live provider credentials have been configured.**

## Scope

A dedicated **server-to-server** JSON endpoint accepts website enquiries and creates normal CRM Leads with the existing `Website` source (ID 2), New status and a configured active owner. The resulting leads remain visible only under the CRM's existing permission scopes and enter ordinary duplicate review/reporting flows.

- POST `/api/integrations/website/leads`
- Content-Type: `application/json`
- Header `X-Website-Key`: confidential 32+ byte shared key
- Header `X-Submission-Id`: unique website-generated reference (8–128 ASCII letters, digits, hyphen, underscore)
- Body: `name`, `phone`, optional `email`, `companyName`, `requirement`

Expected responses: **201** new lead (`id` only), **200** same reference and same normalized body (`alreadyProcessed:true`), **409** same reference with changed body, **400** invalid data, **401** missing/wrong key, **429** throttled, **503** unconfigured/inactive capture owner or Website source.

## Secure environment configuration

Configure privately on the receiving ASP.NET Core **server**, never in React, browser JavaScript, website public HTML, Git or distributed packages:

```text
CRM_WEBSITE_INGEST_KEY=<random 32+ byte secret from secret manager>
CRM_WEBSITE_OWNER_ID=<BIGINT id of an active authorized CRM employee>
```

Both absent => the endpoint is disabled by default (503). Use HTTPS with trusted certificates; deploy behind a reverse proxy with approved IP/forwarding handling and independent perimeter rate limits. Rotate secrets with a controlled integration cutover; the initial version supports one key at a time. On multi-server deployments, prefer shared edge-level throttling in addition to the included per-instance 30 requests/minute/IP limiter.

Only the **website backend** should send requests to this endpoint. Never embed the shared key in a website frontend, form code or a URL. Browser form submission should go to the website's own backend, which validates consent and forwards with the shared key and stable submission ID.

### Example server-to-server request (placeholder only)

```bash
curl -X POST "https://YOUR_CRM_HOST/api/integrations/website/leads" \
  -H "Content-Type: application/json" \
  -H "X-Website-Key: REPLACE_WITH_SERVER_SIDE_SECRET" \
  -H "X-Submission-Id: site-form-unique-12345" \
  --data '{"name":"Sample Customer","phone":"+96512345678","email":"sample@example.test","requirement":"New website"}'
```

A website backend should retry a transient failed request with the **same** submission ID and **same** body. The SQL unique index prevents repeated submissions from creating multiple Leads, including during concurrent submissions. A new submission ID represents a new enquiry—even if the same customer phone/email appears; those are **potential duplicates for manual review**, not automatic deletion.

## Database and audit

EF migration `20261023163000_AddWebsiteLeadSubmission` creates `WebsiteLeadSubmissions` with BIGINT identity, LeadId foreign key, SHA-256 hashed submission reference, SHA-256 payload hash, timestamp, unique submission hash. Successful capture, customer code, receipt and audit record commit in one SQL transaction. No existing Leads table columns or existing Lead IDs are modified.

Do not migrate the user's local source database directly: the last documented `LeadManagement` LocalDB preflight showed five historical migrations and 24 leads. First back up and restore to a separately named staging database, migrate the clone and verify preserved data.

## Security boundaries and future integration work

- The website endpoint is not a browser-open form, nor a Meta/WhatsApp webhook.
- Signature verification and callback schemas for Meta/Facebook/WhatsApp **not implemented** and must not be bypassed by using the website token on untrusted webhook traffic.
- Additional items required before production: consent/privacy retention rules, server-generated submission references, end-to-end deployment/TLS testing, IP forwarding and distributed rate-limit configuration, secret rotation, reliable dead-letter/retry monitoring, and replay/load testing.
- Generic phone/email similarity is handled by the existing Duplicate Review Master and does **not** reject a fresh enquiry purely for matching contact information.

**Production acceptance and local staging migration remain pending.**
