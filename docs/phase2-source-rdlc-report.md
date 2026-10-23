# Phase 2 — Source-wise RDLC Report and Dataset

**Implementation:** `development`, source-report application changes through `28e0c5c`. **CI and deployed staging acceptance:** verify against the exact head before signing off.

## Architecture

This extends the existing Windows .NET Framework ReportViewer rendering pipeline instead of using HTML-to-PDF or a frontend-only table.

```text
SQL Server Leads + LeadSources
  -> RecordAccessService.LeadsAsync(User, reports.view)
  -> filtered server-side source aggregation
  -> typed SourceReportDataSet.xsd (Items DataTable)
  -> Source Summary source-wise.rdlc
  -> ReportRenderer.exe / Microsoft.Reporting.WinForms
  -> API PDF response / React PDF Preview (browser print)
```

- API: `GET /api/reports?type=source-wise`
- Printable RDLC: `reporting/ReportRenderer/Reports/source-wise.rdlc`
- Typed dataset: `reporting/ReportRenderer/Reports/source-wise.xsd`
- Schema contract: `reporting/ReportRenderer/Reports/definitions.json`
- PDF: `GET /api/reports/pdf?type=source-wise`
- CSV: `GET /api/reports/export?type=source-wise`
- Windows renderer: `reporting/ReportRenderer/Program.cs`
- Generator: `scripts/generate-rdlc.py` for checked-in report templates and datasets

**The Source Summary is one row per source, not one row per lead.** DataSet `SourceReportDataSet`, DataTable `Items`, and RDLC DataSetName `Items` must match exactly. The renderer loads the source XSD with `DataSet.ReadXmlSchema`, validates each expected field's .NET type, and renders using `ReportDataSource("Items", table)`.

## Dataset contract

| Column | .NET type | Description |
| --- | --- | --- |
| sourceId | Int64 | Numeric LeadSource ID |
| sourceName | String | Source label, including inactive historical sources |
| leadCount | Int64 | Total leads within selected filters/permission scope |
| qualifiedCount | Int64 | Current status Qualified (3), Proposal (4) or Won (5) |
| wonCount | Int64 | Current status Won (5) |
| conversionRate | Decimal | `wonCount * 100 / leadCount`, shown as percent |
| pipelineAmount | Decimal | Sum of `ProjectAmount` for scoped leads |
| wonAmount | Decimal | Sum of `ProjectAmount` for Won leads only |

**Important:** `ProjectAmount` is a pipeline estimate, **not** signed bookings, collected cash, invoice revenue or recognized revenue. Values retain the existing application's currency conventions. Source summaries are based on **current** lead status; historical stage changes require a separate history-based report.

## Filters and access safety

`from`, `to` use Lead CreatedAtUtc inclusive start/exclusive next-day end; `userId` filters current AssignedToId; `sourceId` filters Lead.SourceId; `statusId` filters current status; `leadGivenBy` uses the existing referrer search. All filters are applied **after** the API obtains the user's effective `reports.view` scope. An additional `leads.view` grant cannot widen this scope.

PDF/CSV invoke the existing report query under the caller's API permissions and enforce the existing row/export bounds. No cross-employee data can be included simply by selecting 'All sources'. Existing 10 reports remain available.

## Validation gates

- Source group aggregates, filtered percentages and pipeline estimates: SQL Server API tests.
- Different users' lead-scope isolation and report restriction: negative-access API tests.
- React source/date/type filter and keyboard navigation: Playwright tests.
- Windows ReportViewer local RDLC-to-PDF: `scripts/verify-rdlc.py` in the IIS packaging workflow (requires Windows .NET Framework ReportRenderer).
- Windows IIS ZIP packaging, screenshot/print quality, paper size/margins and real staging SQL acceptance must be reviewed before production.

## Rule for all future BusinessFlow reports

**Every report requiring print/PDF output must have an explicit field contract, versioned typed `.xsd` DataSet, `.rdlc` layout and a SQL-side scoped/filterable dataset query.** Build typed values and calculations on the server; do not calculate business totals solely from React tables. Verify each RDLC on Windows, preserve role/data-scope filtering, avoid raw SQL concatenation, and document pagination/export caps. All ten pre-existing reports have now also been standardized to strongly typed ADO.NET DataSets. Existing RDLC visual layouts remain unchanged.


## CI verification

- SQL/API and browser: [CI 39963762958](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39963762958): 51/51 SQL/API and 42/42 Playwright PASS.
- Windows PDF rendering: [CI 39963948187](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39963948187): all 11 RDLC files rendered to PDFs using their typed .xsd DataSets, PASS.
- Local SQL migration, IIS staging print review and production acceptance remain pending.
