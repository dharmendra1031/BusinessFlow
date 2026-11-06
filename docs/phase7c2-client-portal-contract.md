# Resmoh IT — Phase 7C2: Verified Client Portal contract

**Status:** Design and access-control contract only. Do **not** claim a working client login, email delivery, quotation access, UAT or production deployment on the basis of this document. Existing Phase 7C1 receipt-status feature remains unchanged.

## Goal and existing components

Public website: `resmoh.in`, client-facing pages under `/client/`. Internal CRM: BusinessFlow on proposed `app.resmoh.in`. Keep private employee UI and customer portal authorization isolated.

Reuse: Customer + CustomerContact, Leads.CustomerId, SalesQuotations -> Leads, SalesContracts -> SalesQuotations, SalesInvoices -> SalesContracts, DevelopmentProjects -> Leads. Existing quotation/finance Admin endpoints **must not** be exposed via public proxy or reused for client auth.

**Important:** Public website enquiries currently create Leads, not verified Customer accounts. An Admin must resolve duplicates, associate the Lead to the canonical Customer and approve an existing active CustomerContact before granting portal access. Phone/email text in an enquiry never counts as identity proof.

## Access and identity

One client-access row references an explicitly approved active Customer and CustomerContact.

`ClientPortalAccounts` (new table, numeric `BIGINT IDENTITY` PK):
- `Id`, `CustomerId` FK, `CustomerContactId` FK.
- `NormalizedEmail` (must match the active contact email), `IsActive`, `CreatedById` (Admin), `CreatedAtUtc`, `RevokedAtUtc`, `SessionVersion`, SQL `rowversion`.
- Unique `CustomerContactId`; stable customer/contact mapping. Reject ambiguous active contact email links across customers until reviewed by Admin. No public self-service registration.
- Verify on **every** authenticated request: account active, no revocation, customer active/not merged, contact active and still belongs to account's customer, normalized contact email unchanged, and session version current.

`ClientLoginChallenges` (new table, numeric `BIGINT IDENTITY` PK):
- `Id`, `ClientPortalAccountId` FK, `CodeHash`, `ExpiresAtUtc`, `AttemptCount`, `UsedAtUtc`, `CreatedAtUtc`.
- Email OTP generated cryptographically, delivered only to the approved contact email using a configured server-side mail transport. The OTP is **not** an entity ID and must never be stored or logged in plaintext.
- One active OTP per account; expiry at most 10 minutes; max 5 failed attempts before blocking; resend revokes old OTP; replay forbidden. Hash OTP with a server-side secret/pepper and compare in constant time.
- Rate-limit IP and account/email request attempts. Unknown account, inactive account and undisclosed email return indistinguishable generic responses. Fail closed when outbound mail settings are missing—never return an OTP in the API or logs.
- Verification consumes code transactionally and starts a dedicated `Resmoh.Client` Secure, HttpOnly, SameSite=Strict cookie. Client session uses a separate authentication scheme/data-protection purpose from existing `CRM.Session` employee JWTs, explicit expiry, CSRF protection and sign-out/revocation. **Never** issue an Admin/Employee role or allow ClientPortal cookie to authenticate `/api/admin/*` or internal routes.

For deployment, make client API paths available on the **same origin** as the public portal through a strictly allowlisted IIS reverse proxy (only `/api/client/*`); alternatively configure audited credentialed CORS and cookie scope explicitly. Do not add an unrestricted proxy to the CRM API. HTTPS, forwarding headers, stable data-protection key storage, mail service, edge rate limiting and monitoring are prerequisites.

## Endpoint contract (to implement in steps)

| Route | Authorization | Behavior |
| --- | --- | --- |
| `POST /api/admin/client-access` | CRM Admin only | Approve active Customer/Contact mapping and enable account; idempotent for same contact |
| `POST /api/admin/client-access/{id}/revoke` | CRM Admin only | Revoke access, bump session version, audit; immediate block |
| `POST /api/client/auth/request-code` | Anonymous, rate-limited | Generic accepted response, deliver to verified mapped email only |
| `POST /api/client/auth/verify-code` | Anonymous, rate-limited | Atomic OTP consumption; separate client session |
| `POST /api/client/auth/logout` | Client | Revoke cookie / sign out with CSRF protection |
| `GET /api/client/me` | Client | Client profile (minimal safe fields) |
| `GET /api/client/quotations` | Client | Customer-scoped, **Sent/Accepted/Rejected** only; do not show Draft/PendingApproval |
| `GET /api/client/invoices` | Client | Customer-scoped issued invoices; amounts, due date, status only |
| `GET /api/client/projects` | Client | Only projects explicitly published/approved for the client; no internal tasks, comments, costs or employee worklogs |

All customer-scoped queries derive CustomerId from the **validated session**. Never take a trusted `customerId` from query/body or render records after customer merge/revocation. Use server-side projection, pagination and typed status checks; never reuse broad Admin DTOs or raw entity serialization.

Quotation approval, contract signing, payments and UAT **remain separate later phases**. No automatic quotation submission/acceptance, invoice posting or money movement via this read-only portal.

## Implementation sequence

1. **7C2-A — Account/OTP:** additive migration + account management + secure email OTP and dedicated session, with user/admin audit and disabled-by-default configuration. Keep current website receipt tracking working. No client financial data exposed yet.
2. **7C2-B — Read-only records:** quotations/invoices projection only after cross-customer security tests and active-session revocation tests. Explicitly verify all customer associations via Lead -> Customer and Quote -> Contract -> Invoice.
3. **7C2-C — Client-facing UI:** `/client/login/` and authenticated overview; no fake documents/demo stats; responsive, accessible, clear empty/error states. Full same-origin reverse proxy staging test.
4. **7C3 onwards:** revision/acceptance with rowversion + terms evidence, approved project milestones/UAT, verified payments, support and AMC.

## Mandatory security regression tests

- One valid OTP activates only the linked contact; wrong/expired/replayed OTP, resends and brute-force attempts rejected.
- Anonymous and 7C1 receipt-only user cannot see any quotation or invoice; CRM.Session employee cookie is **not** a ClientPortal session; ClientPortal cookie cannot call internal/admin endpoints.
- Client A cannot retrieve Client B's records by changing any identifier or URL; no data leak through list/count/search/export.
- Draft quotation and unissued invoice stay private.
- Disabling the portal account, customer, contact or changing the contact email revokes existing sessions; merged customers fail closed.
- Ambiguous duplicate contacts, concurrent invites/verification, missing SMTP, unavailable database, CSRF and cookie flags.
- Customer scoping when lead is unlinked or reassigned to a different customer, and after quote revision.
- Idempotent migrations, bigint identity keys, staging restore, audit trace, no plaintext OTP/email keys in packages/logs.

## Release blockers

No live `resmoh.in` deployment yet. Before public launch: production-owned SMTP/email sender; staging DNS/HTTPS/IIS plan and reverse proxy; final privacy notice and retention policy; consent for client communications; approved customer-access enrollment workflow; independent security and UAT sign-off.

**Policy:** Do not open financial records by reusing Phase 7C1 browser receipt or by trusting an enquiry's email address. The authentication milestone must pass first.
