# Customer Master — Phase 1 foundation

Branch: development. This is an admin-only, additive first increment, not a complete commercial module.

- `Customers` has a numeric BIGINT identity key, contact details, active state and SQL Server rowversion.
- `CustomerContacts` has multiple named contacts per customer; one can be marked primary using a serializable update.
- `Leads.CustomerId` is nullable, indexed and references `Customers`. Existing lead rows remain unlinked after migration.
- A customer can group multiple existing leads and their respective development projects. The existing **one project per Won lead** rule is preserved. Direct project creation from customer is **not** included.
- The admin-only API supports customer CRUD without deleting history, contacts and explicit lead link/unlink with audit logs and rowversion checks.
- No automatic matching or bulk merge of customers/leads takes place. Unlinking does not delete or change leads, projects, tasks, invoices or statuses.
- Admin can use **Customer master** in the React sidebar; permission design for Sales/Account Managers is still pending.

## Deployment safety

GitHub CI uses temporary disposable SQL Server databases; do not apply migrations to a real customer database without a validated backup, an isolated restore, and staging upgrade checks. The migration adds only two tables, a nullable FK column and indexes. Existing database/customer identity must never be overwritten.

## Next increments

Customer-specific data scope and role matrix, complete paginated linked history, customer merge governance, direct commercial quotations and contracts, invoice/payment integration, and client support/AMC.

## Billing details and duplicate checks — incremental update

- Optional legal/billing name, address, city, state, postal code, country and Indian GSTIN fields are supported by API and UI. A blank GSTIN is allowed for customers outside India's GST system.
- SQL migration `20261025110000_AddCustomerBilling` adds nullable columns and a persisted phone-normalization column with non-unique indexes; existing customer, lead and project data is not rewritten.
- Admin customer creation/update returns HTTP 409 with matching customer identifiers if email (case-insensitive), normalized phone, or GSTIN matches another customer. It does **not** automatically merge records. Contact persons can legitimately share contact information.
- Customer audit history is paginated and contains Customer/Contact actions plus explicit lead link/unlink events; the UI can load earlier entries.
- Existing one-project-per-Won-lead semantics and employee access restrictions remain unchanged. Customer roles and data-scope permissions need separate reviewed design.
- Duplicate matching is an application-level review check, not a global uniqueness guarantee under simultaneous requests. Manual exception/merge policy and stress tests are pending.
- Real IIS staging migration and backup/restore acceptance are still pending.

