# Resmoh IT — Phase 7C2-B Client Documents (read-only)

## Scope

The verified client account can view **only** the current published quotation revision and non-void commercial invoices related to its own Customer. This adds no schema or financial writes.

Routes, all behind the independent `ClientPortal` cookie authentication scheme:
- `GET /api/client/quotations?page=1`
- `GET /api/client/quotations/{id}`
- `GET /api/client/invoices?page=1`
- `GET /api/client/invoices/{id}`

Lists are sorted by descending numeric ID and capped at 20 records/page. All keys are existing `BIGINT IDENTITY` IDs. Data is projected to safe customer-facing fields and no employee, audit, internal Lead IDs, rowversions or Admin DTOs are serialized.

## Authorization boundary

- The validated `ClientPortal` cookie identifies a `ClientPortalAccount`; the server loads `CustomerId` from this account on **every read**. Request body, query parameters and guessed IDs cannot choose a Customer.
- Every quotation must resolve through `Quotation -> Lead.CustomerId` to the current account. Only `Sent`, `Accepted`, `Rejected` quotation states are visible, and only the current revision is returned. Draft, PendingApproval and Approved remain private.
- Every invoice must resolve through `Invoice -> Signed Contract -> Accepted Quotation -> Lead.CustomerId`. Only `Issued`, `PartiallyPaid`, `Paid`, `RefundDue`, `Credited` invoices are shown. Void and unsigned/unaccepted records are hidden.
- The original `ClientPortal` authentication validation still checks account activation and version, customer/contact activation, merge state and authorized contact email. Invalid/revoked sessions receive 401; unknown/other-customer document IDs receive 404.
- Internal `/api/admin/*` routes remain JWT/Admin only. Public site proxies only explicit client read and authentication routes, never arbitrary CRM URLs. Sensitive responses must not be cached.

## UI

`/client/account/` shows two independent compact lists with pagination, clear empty/error states and read-only document details. Dynamic text is assigned with `textContent` rather than untrusted HTML. No demo data, payment buttons, contract signing or downloadable PDFs were added.

## Release gates

Code tests verify customer isolation, unpublished/void filters, ID guessing, anonymous/staff session denial and revocation, plus strict public proxy routing. CI build and integration checks do **not** replace staging acceptance.

Still pending: deployed staging website+CRM with correct HTTPS reverse proxy and preserved Data Protection keys, real SMTP verification, customer data sign-off, financial disclosure review, mobile/browser accessibility review, production legal/privacy notice and rollback rehearsal.

Later phases: client approval, contract signing, milestones, invoice PDFs subject to approval, payment gateway and support. None is enabled by this read-only phase.
