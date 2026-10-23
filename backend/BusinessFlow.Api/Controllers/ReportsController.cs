using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reports")]
public sealed class ReportsController(AppDbContext db, IConfiguration configuration, RecordAccessService records) : ControllerBase
{
    private string Action => RolePermissionService.ForRequest(Request) ?? "reports.view";
    private static readonly string[] ReportTypes =
    [
        "customers", "user-customers", "active", "closed", "not-interested",
        "followups", "overdue", "project", "user-project", "created",
        "source-summary", "source-details"
    ];

    [HttpGet("pdf")]
    [RequirePermission("reports.pdf")]
    public async Task<IActionResult> Pdf(string type = "customers", DateTime? from = null,
        DateTime? to = null, long? userId = null, long? statusId = null, string? leadGivenBy = null,
        long? sourceId = null)
    {
        var first = await Get(type, from, to, userId, statusId, leadGivenBy, 1, 200, sourceId);
        if (first is not OkObjectResult ok) return first;
        var data = JsonSerializer.SerializeToElement(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var total = data.GetProperty("total").GetInt32();
        var maxRows = configuration.GetValue<int?>("Reports:MaxPdfRows") ?? 5000;
        if (maxRows < 1) maxRows = 5000;
        if (total > maxRows)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { message = $"PDF exceeds the {maxRows:N0} row limit. Narrow the filters." });
        var rows = new List<JsonElement>(total);
        for (var page = 1; (page - 1) * 200 < total; page++)
        {
            if (page > 1)
            {
                var next = await Get(type, from, to, userId, statusId, leadGivenBy, page, 200, sourceId);
                data = JsonSerializer.SerializeToElement(((OkObjectResult)next).Value,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            rows.AddRange(data.GetProperty("rows").EnumerateArray());
        }
        var renderer = configuration["Reports:RendererPath"];
        if (string.IsNullOrWhiteSpace(renderer))
            renderer = Path.Combine(Directory.GetCurrentDirectory(), "ReportRenderer", "ReportRenderer.exe");
        if (!System.IO.File.Exists(renderer))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "PDF report renderer is unavailable." });
        var output = Path.Combine(Path.GetTempPath(), "crm-report-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(renderer)
                {
                    ArgumentList = { type, output },
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(rows));
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            if (process.ExitCode != 0 || !System.IO.File.Exists(output))
            {
                HttpContext.RequestServices.GetRequiredService<ILogger<ReportsController>>()
                    .LogError("PDF renderer failed for {ReportType} with exit code {ExitCode}", type, process.ExitCode);
                return StatusCode(StatusCodes.Status502BadGateway,
                    new { message = "PDF report generation failed." });
            }
            return File(await System.IO.File.ReadAllBytesAsync(output), "application/pdf", enableRangeProcessing: false);
        }
        finally { if (System.IO.File.Exists(output)) System.IO.File.Delete(output); }
    }

    [HttpGet("export")]
    [RequirePermission("reports.export")]
    public async Task<IActionResult> Export(string type = "customers", DateTime? from = null,
        DateTime? to = null, long? userId = null, long? statusId = null, string? leadGivenBy = null,
        long? sourceId = null)
    {
        var first = await Get(type, from, to, userId, statusId, leadGivenBy, 1, 200, sourceId);
        if (first is not OkObjectResult ok) return first;
        var data = JsonSerializer.SerializeToElement(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var total = data.GetProperty("total").GetInt32();
        var maxRows = configuration.GetValue<int?>("Reports:MaxExportRows") ?? 100000;
        if (maxRows < 1) maxRows = 100000;
        if (total > maxRows)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { message = $"Export exceeds the {maxRows:N0} row limit. Narrow the filters." });

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{type}-report.csv\"";
        await using var writer = new StreamWriter(Response.Body, new UTF8Encoding(true), bufferSize: 16384, leaveOpen: true);
        var watch = Stopwatch.StartNew();
        string[]? columns = null;
        for (var page = 1; (page - 1) * 200 < total; page++)
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(60))
                throw new TimeoutException("Report export exceeded 60 seconds.");
            if (page > 1)
            {
                var next = await Get(type, from, to, userId, statusId, leadGivenBy, page, 200, sourceId);
                data = JsonSerializer.SerializeToElement(((OkObjectResult)next).Value,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            foreach (var row in data.GetProperty("rows").EnumerateArray())
            {
                if (columns is null)
                {
                    columns = row.EnumerateObject().Select(p => p.Name).ToArray();
                    await writer.WriteLineAsync(string.Join(',', columns.Select(Csv)));
                }
                await writer.WriteLineAsync(string.Join(',', columns.Select(c =>
                    Csv(row.GetProperty(c).ToString()))));
            }
            await writer.FlushAsync();
            HttpContext.RequestAborted.ThrowIfCancellationRequested();
        }
        return new EmptyResult();
    }

    private static string Csv(string value)
    {
        var first = value.TrimStart().FirstOrDefault();
        if (first is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    [HttpGet]
    [RequirePermission("reports.view")]
    public async Task<IActionResult> Get(string type = "customers", DateTime? from = null,
        DateTime? to = null, long? userId = null, long? statusId = null, string? leadGivenBy = null,
        int page = 1, int pageSize = 50, long? sourceId = null)
    {
        if (page < 1 || pageSize is < 1 or > 200 || (long)(page - 1) * pageSize > int.MaxValue)
            return BadRequest(new { message = "Invalid pagination." });
        if (!ReportTypes.Contains(type)) return BadRequest(new { message = "Unknown report." });
        if (sourceId is <= 0) return BadRequest(new { message = "Invalid lead source." });
        if (leadGivenBy?.Length > 150)
            return BadRequest(new { message = "Lead referrer filter must not exceed 150 characters." });
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            return BadRequest(new { message = "Invalid date range." });

        var leads = (await records.LeadsAsync(User, Action)).AsNoTracking();
        if (userId.HasValue) leads = leads.Where(l => l.AssignedToId == userId);
        if (statusId.HasValue) leads = leads.Where(l => l.StatusId == statusId);
        if (sourceId.HasValue) leads = leads.Where(l => l.SourceId == sourceId);
        if (!string.IsNullOrWhiteSpace(leadGivenBy))
            leads = leads.Where(l => l.LeadGivenBy != null && l.LeadGivenBy.Contains(leadGivenBy));

        if (type == "followups" || type == "overdue")
        {
            var leadIds = leads.Select(l => l.Id);
            var followUps = db.LeadFollowUps.AsNoTracking().Where(f => leadIds.Contains(f.LeadId));
            if (type == "overdue")
                followUps = followUps.Where(f => f.CompletedAtUtc == null && f.DueAtUtc < DateTime.UtcNow);
            if (from.HasValue) followUps = followUps.Where(f => f.DueAtUtc >= from.Value.Date);
            if (to.HasValue) followUps = followUps.Where(f => f.DueAtUtc < to.Value.Date.AddDays(1));
            var total = await followUps.CountAsync();
            var rows = await followUps.OrderByDescending(f => f.DueAtUtc).ThenByDescending(f => f.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(f => new
                {
                    followUpId = f.Id, customerId = f.Lead.Id,
                    customerCode = f.Lead.CustomerCode, customerName = f.Lead.Name,
                    followUpDate = f.CompletedAtUtc, nextFollowUpDate = f.DueAtUtc,
                    remarks = f.Notes, userId = f.AssignedToId
                }).ToListAsync();
            return Ok(new { type, total, page, pageSize, rows });
        }

        if (from.HasValue) leads = leads.Where(l => l.CreatedAtUtc >= from.Value.Date);
        if (to.HasValue) leads = leads.Where(l => l.CreatedAtUtc < to.Value.Date.AddDays(1));

        leads = type switch
        {
            "active" => leads.Where(l => l.StatusId <= 4 || l.StatusId == 7),
            "closed" => leads.Where(l => l.StatusId == 5 || l.StatusId == 8),
            "not-interested" => leads.Where(l => l.StatusId == 6 || l.StatusId == 9),
            _ => leads
        };

        if (type == "source-summary")
        {
            // All metrics use the caller's effective report data scope.
            // Qualified and Won mean the lead's CURRENT status, not historical transitions.
            var groups = leads.GroupBy(l => new { l.SourceId, SourceName = l.Source.Name })
                .Select(g => new
                {
                    sourceId = g.Key.SourceId,
                    sourceName = g.Key.SourceName,
                    leadCount = g.Count(),
                    qualifiedCount = g.Count(l => l.StatusId == 3),
                    wonCount = g.Count(l => l.StatusId == 5),
                    conversionPercent = g.Count(l => l.StatusId == 5) * 100m / g.Count(),
                    projectAmount = g.Sum(l => l.ProjectAmount ?? 0m)
                });
            var total = await groups.CountAsync();
            var rows = await groups.OrderBy(x => x.sourceName).ThenBy(x => x.sourceId)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { type, total, page, pageSize, rows });
        }

        if (type == "source-details")
        {
            var total = await leads.CountAsync();
            var rows = await leads.OrderByDescending(l => l.CreatedAtUtc).ThenByDescending(l => l.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(l => new
                {
                    sourceName = l.Source.Name,
                    customerCode = l.CustomerCode,
                    customerName = l.Name,
                    assignedUser = l.AssignedTo == null ? "" : l.AssignedTo.FullName,
                    leadStatus = l.Status.Name,
                    projectAmount = l.ProjectAmount,
                    createdAtUtc = l.CreatedAtUtc
                }).ToListAsync();
            return Ok(new { type, total, page, pageSize, rows });
        }

        if (type is "user-customers" or "user-project")
        {
            var total = await leads.Select(l => l.AssignedToId).Distinct().CountAsync();
            var rows = await leads.GroupBy(l => new { l.AssignedToId, Name = l.AssignedTo == null ? "" : l.AssignedTo.FullName })
                .Select(g => new
                {
                    userId = g.Key.AssignedToId, userName = g.Key.Name,
                    customerCount = g.Count(), projectAmount = g.Sum(l => l.ProjectAmount ?? 0m)
                }).OrderByDescending(g => g.customerCount).ThenBy(g => g.userId)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { type, total, page, pageSize, rows });
        }

        var count = await leads.CountAsync();
        var customers = await leads.OrderByDescending(l => l.CreatedAtUtc).ThenByDescending(l => l.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new
            {
                customerId = l.Id, l.CustomerCode, customerName = l.Name, mobileNumber = l.Phone,
                l.CompanyName, l.Address, l.LeadGivenBy, l.ProjectAmount,
                leadStatus = l.Status.Name, l.LastFollowUpDate, l.NextFollowUpDate,
                createdByUserId = l.CreatedById, assignedToUserId = l.AssignedToId,
                l.CreatedAtUtc
            }).ToListAsync();
        return Ok(new { type, total = count, page, pageSize, rows = customers });
    }
}
