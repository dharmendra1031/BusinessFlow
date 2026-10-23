# Phase 2 — Website Lead Capture Foundation

**Status:** Implementation on development feature branch; CI verification required. No live provider credentials are configured.

## API contract

A **website backend** (never browser JavaScript) can POST JSON to `/api/integrations/website/leads` over HTTPS. Requests must be signed using HMAC-SHA256 and include a timestamp and unique event reference. This is not a Facebook/Meta/WhatsApp webhook endpoint.

Required headers:
- `Content-Type: application/json`
- `X-Website-Timestamp`: Unix UTC timestamp in seconds (max ±5 minutes clock skew)
- `X-Website-Signature`: `sha256=<64 lowercase/uppercase hexadecimal characters>`

HMAC message bytes are exactly: `UTF8(timestamp + ".")` followed by the **raw, unmodified JSON request bytes**. Calculate `HMACSHA256(SigningSecret, messageBytes)`, encode as 64 hexadecimal characters, and prefix with `sha256=`. The timestamp must be synchronized and the request body must be byte-identical between signing and transmission.

JSON example:
```json
{
  "eventId": "website-contact-123456",
  "name": "Example customer",
  "phone": "+96512345678",
  "email": "visitor@example.test",
  "companyName": "Example company",
  "requirement": "New website",
  "consentAccepted": true
}
```

`eventId` must be 8–100 ASCII letters/digits/underscore/hyphen and stable across retries. `consentAccepted` must be `true`; collect an appropriate lawful contact-consent signal on the originating website. The API validates name length, phone format and optional email/field lengths; 16 KB request body max.

## Private server configuration

**Disabled by default**. Only enable after staging security acceptance and provision these values through ASP.NET Core configuration on the receiving server (environment variable form shown):

```text
WebsiteIntake__Enabled=true
WebsiteIntake__SigningSecret=<strong random signing key, 32+ UTF-8 bytes>
WebsiteIntake__OwnerUserId=<BIGINT ID of active CRM employee>
```

Keep the signing secret **only on the website server and CRM server**, not in React/public website frontend, query parameters, Git or appsettings. Rotate through a coordinated key-rollover with the website backend; v1 supports one active key. Require trusted HTTPS and apply reverse-proxy/IP forwarding configuration carefully. Per-instance requests are limited to 30/minute per remote IP by ASP.NET rate limiting; an upstream gateway/WAF or shared limiter is necessary for distributed deployment.

## Responses and replay handling

- **202 Accepted**: valid new submission, or exact retry already ingested (response intentionally does not disclose Lead ID)
- **400**: invalid JSON or business/consent validation
- **401**: missing/invalid signature, expired timestamp or invalid signature format
- **409**: same `eventId` re-sent with a different raw body
- **429**: rate-limited
- **503**: integration disabled/unconfigured or configured owner/Website lead source unavailable

The SQL table `WebsiteLeadSubmissions` stores SHA256 hashes of `eventId` and raw request bytes, with a **unique submission hash** and FK to the created Lead. Lead creation, customer code, receipt and audit log are one SQL transaction. A replay with the same ID and identical body returns 202 and does not create a second Lead. A different event ID creates a separate Lead—even with matching contact details—and is a candidate for the existing manual Duplicate Review Master, never auto-merged. Created Lead receives source **Website** (ID 2), New status, and configured active owner.

## Testing, staging and limitations

Automated SQL API tests should cover valid HMAC, replay, payload mismatch, timestamp expiry, consent denial, source inactive and disabled-by-default. The team must verify the exact GitHub CI run before declaring it passed.

This release does **not** provide a public embeddable form, real Facebook/WhatsApp provider integration, live user consent UI, provider secret rotation, distributed throttling, dead-letter monitoring or final production acceptance.

**The user's LocalDB `LeadManagement` database was last observed with five migrations and 24 leads. No local migration or staging restore was executed from this task.** Use the isolated backup/restore and migration acceptance checklist before applying Phase 2 migrations anywhere outside the disposable CI SQL Server.
