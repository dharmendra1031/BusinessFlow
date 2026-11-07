# Phase 7C2-D — Client project milestone publication

Only Admin may publish a milestone for client viewing. Newly created milestones are private, and editing an existing milestone automatically hides it until the Admin re-approves. Publication changes are audited.

## API and disclosure

- Admin: `PATCH /api/projects/{projectId}/planning/milestones/{milestoneId}/publication` with `{ "isPublished": true/false }`.
- Client: `GET /api/client/projects?page=1` requires the separate ClientPortal cookie.
- Results are capped at 20 published milestones per page. The server resolves the customer through the verified client account and filters by `Milestone -> DevelopmentProject -> Lead.CustomerId`.
- Only milestone title, status, due date, published time, project title and numeric project/milestone IDs are exposed. No internal description, tasks, employees, sprints, costs, comments or worklogs.
- Publishing is blocked unless the linked Lead belongs to an active, unmerged Customer. If a Lead is reassociated, queries immediately apply the new customer relationship.
- The original client account validation continues to block inactive, revoked or merged customer sessions.

## Database

Additive migration `20261107120000_AddPublishedMilestones` adds nullable `PublishedAtUtc` to the existing `ProjectMilestones` table. NULL means internal-only. Existing records are not published. No UUIDs or new entity IDs.

## Rollout

CI coverage checks anonymous access, staff/client session separation, customer A/B isolation, editing revokes publication, unpublish and audit, plus the public website route allowlist.

Before staging acceptance, confirm the project/milestone titles are suitable for external sharing. Production remains disabled until staging database backup/restore, correct HTTPS/SMTP/IIS configuration, client disclosure review and authorized release sign-off. No payment, contract acceptance or support system was added.
