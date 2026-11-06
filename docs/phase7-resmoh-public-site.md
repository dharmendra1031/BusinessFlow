# Resmoh IT public website — Phase 7A / 7B

**Status:** Code prepared on BusinessFlow `development`. Do not describe this as deployed to `resmoh.in`; live domain/hosting and an authorized privacy/legal review are still pending.

## Scope

- Professional public website: `/` (services, example industries, process); `/start-project/` (three-step project enquiry); `/privacy/` (initial enquiry notice, **not a final legal privacy policy**).
- Separate ASP.NET Core .NET 10 app: `public-site/Resmoh.Web`, publishable for Windows IIS **out-of-process**, isolated from the private BusinessFlow SPA.
- Server-side POST `/api/project-requests`, validates details, verifies allowed browser Origin, rejects oversize/invalid requests, uses a 5-per-minute per-IP limiter and a honeypot.
- Server only forwards signed requests to the **existing** `POST /api/integrations/website/leads` using HMAC-SHA256, following `docs/phase2-website-intake.md`. No duplicate CRM or new SQL tables. Public success never exposes internal lead IDs or client data.
- New CRM Lead, Website source, routing/assignee, duplicate-submission hash and audit are handled by the existing BusinessFlow controller. The external event key is numeric-looking and used exclusively for HTTP idempotency; **all database entity IDs remain BIGINT IDENTITY**.
- Client Portal, project tracking, client account creation, AI-generated quotations, payment gateway, appointment booking and outbound WhatsApp/email are **not** implemented in this increment.

## IIS and secrets

Deploy the **separate** `Resmoh-PublicSite-IIS.zip` package to an approved public IIS website. Do not replace the private BusinessFlow IIS package or point the public website at a production database. Public app settings/environment:

```text
ASPNETCORE_ENVIRONMENT=Production
PublicSite__Origin=https://resmoh.in
BusinessFlow__IntakeUrl=https://YOUR_AUTHORIZED_CRM_HOST/api/integrations/website/leads
BusinessFlow__SigningSecret=<same secret as the CRM website intake; minimum 32 UTF-8 bytes>
```

The CRM's **own private server configuration** must separately contain:

```text
WebsiteIntake__Enabled=true
WebsiteIntake__SigningSecret=<same signing secret, never shared with browser>
WebsiteIntake__OwnerUserId=<existing active CRM user's numeric ID>
```

Keep both secrets in locked-down server configuration, not in Git, JavaScript, public assets, logs, or chat. Put the CRM behind HTTPS and network controls as appropriate. The website must call the **server-side** API, not the CRM intake from JavaScript. Backend returns HTTP 503 until integration configuration is valid. Do not silently store or pretend to accept requests while disconnected.

**Important:** ASP.NET Core per-instance rate limiting cannot stop distributed bot abuse on multiple instances. Configure edge/WAF rate limits and DNS/TLS/firewall/proxy rules before a production launch. Keep the public application SQL-free and isolated from private CRM authentication cookies.

## Validation and staging acceptance

1. Build `public-site/Resmoh.Web/Resmoh.Web.csproj` and run `ResmohWebsiteTests` with the main test suite; tests validate static content, server-only secret, signed payload and missing configuration.
2. Inspect the separate staging ZIP, no private credentials, source control leaks or fake reference customers.
3. Set up **separate staging site**, staging origin and matching secrets to an authorized staging CRM instance with safe test data. Confirm HMAC replay/idempotency, consent, contact validation and Lead routing.
4. Verify 320px/375px/mobile, tablet and desktop layouts; keyboard navigation, screen reader labels, 429, 503 and offline/retry behaviour. Do not claim visual acceptance from code/build checks alone.
5. Complete company identity, contact details, full privacy notice, consent wording, retention policy, security operations and Google Search Console setup before publishing. Ensure no site presently hosted at `resmoh.in` is overwritten without an approved migration plan.

## Future Phase 7C

Client identity verification and authentication; access-scoped client requests; quotation view and review; consent/notification history; later contract, invoices and project acceptance. Keep public users isolated from Admin/Employee roles and internal financial details.
