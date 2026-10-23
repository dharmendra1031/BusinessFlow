# Phase 2 — Follow-up Reminder Management (in-app checkpoint)

**Scope:** Authenticated in-app alerts for due and overdue follow-ups. A reminder is an existing scheduled follow-up; no new Lead or external message is created.

## Implemented

- Scoped `GET /api/followups/reminders` protected by `followups.view`, filtered to the currently authenticated employee's pending follow-ups **and** the leads their effective custom-role/data scope permits.
- Bounded `withinHours` (1–72), pagination (max 50), due-time ordering and `overdue` / `upcoming` counts.
- CRM header displays an indicator when reminders are due; the employee can open the existing Follow-ups page to view the due items.
- In-app reminder panel refreshes every 60 seconds while the page is open. After rescheduling/completing a follow-up, the panel reloads.
- Existing lead reassignment logic already moves **pending** follow-ups to the new owner, retaining completed history with the original assignee.
- SQL/API regression test checks overdue vs upcoming windows, incomplete vs completed records, tenant-independent per-lead permission scope, and visibility after owner transfer.
- Playwright test exercises the header indicator and displayed reminder queue (mocked HTTP responses).

## Explicit limits

This milestone **does not send scheduled email, SMS, push or WhatsApp messages**; reminders refresh only while the CRM is open. It does not implement external retry queues, acknowledgement, out-of-office/quiet hours, consent/opt-out preferences, or background delivery. Do not describe it as an always-on notification service.

The app's existing Manual Follow-up Create, Edit and Complete actions remain unchanged. Existing database records, numeric IDs and UI theme stay intact. No schema migration was added for this checkpoint.

## Verification and release boundary

- Application code on `development` (latest test correction at `48e6662`).
- [GitHub SQL/API and browser CI](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39935263037) — verify the **final workflow conclusion** before marking this milestone CI Verified.
- [Windows IIS package run](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39935262993) — build/package verification; not a live deployment.
- Earlier test-only failure at `4ce31be`: C# variable naming collision in regression test; fixed in `48e6662`. That failure is not a production security defect.
- User's local `(localdb)\\MSSQLLocalDB` / `LeadManagement` preflight previously showed 24 Leads and five migrations. Actual isolated backup/restore and local staging acceptance remain pending; **do not apply the full set of newer Phase 1/2 migrations against source until that gate is complete**.

## Next milestone

Durable outbound reminder delivery: background scheduler, per-recipient channel preferences and opt-out, consent tracking, idempotent retries and failure audit. This requires selecting delivery providers and defining business and privacy rules.
