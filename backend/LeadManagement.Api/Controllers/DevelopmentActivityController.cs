using LeadManagement.Api.Contracts;
using LeadManagement.Api.Data;
using LeadManagement.Api.Models;
using LeadManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadManagement.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:long}")]
public sealed class DevelopmentActivityController(AppDbContext db) : ControllerBase
{
    private static readonly string[] Severities = ["Low", "Medium", "High", "Critical"];
    private static readonly string[] BugStatuses = ["Open", "InProgress", "Resolved", "Closed"];

    private IQueryable<DevelopmentProject> VisibleProjects()
    {
        var projects = db.DevelopmentProjects.AsQueryable();
        if (User.IsInRole("Admin")) return projects;
        var id = LeadAccess.UserId(User);
        if (User.IsInRole("Manager"))
            return projects.Where(p => p.CreatedById == id || p.OwnerId == id ||
                p.Owner.ManagerId == id || p.Tasks.Any(t => t.AssignedToId == id) ||
                db.ProjectBugs.Any(b => b.ProjectId == p.Id && b.AssignedToId == id));
        return projects.Where(p => p.OwnerId == id || p.Tasks.Any(t => t.AssignedToId == id) ||
            db.ProjectBugs.Any(b => b.ProjectId == p.Id && b.AssignedToId == id));
    }

    private bool CanManage(DevelopmentProject project)
    {
        if (User.IsInRole("Admin")) return true;
        if (!User.IsInRole("Manager")) return false;
        var id = LeadAccess.UserId(User);
        return project.CreatedById == id || project.OwnerId == id || project.Owner.ManagerId == id;
    }

    private async Task<bool> ValidAssignee(long? assigneeId)
    {
        if (assigneeId is null) return true;
        var member = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == assigneeId && u.IsActive);
        if (member is null) return false;
        var id = LeadAccess.UserId(User);
        return User.IsInRole("Admin") ||
            (User.IsInRole("Manager") && (member.Id == id || member.ManagerId == id));
    }

    private void Audit(string action, string table, long id)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Action = action, TableName = table, RecordId = id,
            UserId = LeadAccess.UserId(User)
        });
    }

    [HttpGet("bugs")]
    public async Task<IActionResult> Bugs(long projectId, int page = 1, int pageSize = 50)
    {
        if (page < 1 || pageSize is < 1 or > 200 || (long)(page - 1) * pageSize > int.MaxValue)
            return BadRequest(new { message = "Invalid pagination." });
        if (!await VisibleProjects().AnyAsync(p => p.Id == projectId)) return NotFound();
        var query = db.ProjectBugs.AsNoTracking().Where(b => b.ProjectId == projectId);
        var total = await query.CountAsync();
        var openCount = await query.CountAsync(b => b.Status != "Closed");
        var results = await query.OrderBy(b => b.Status == "Closed")
            .ThenByDescending(b => b.CreatedAtUtc).ThenByDescending(b => b.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(b => new
            {
                b.Id, b.TaskId, b.Title, b.Description, b.Severity, b.Status,
                b.AssignedToId, AssignedTo = b.AssignedTo == null ? null : b.AssignedTo.FullName,
                b.ReportedById, Reporter = b.ReportedBy.FullName,
                b.CreatedAtUtc, b.UpdatedAtUtc, b.ClosedAtUtc
            }).ToListAsync();
        return Ok(new { total, openCount, page, pageSize, items = results });
    }

    [HttpPost("bugs")]
    public async Task<IActionResult> AddBug(long projectId, CreateProjectBugRequest request)
    {
        var project = await VisibleProjects().Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == projectId);
        if (project is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length < 2 ||
            !Severities.Contains(request.Severity))
            return BadRequest(new { message = "Enter a valid bug title and severity." });
        if (request.TaskId.HasValue &&
            !await db.DevelopmentTasks.AnyAsync(t => t.Id == request.TaskId && t.ProjectId == projectId))
            return BadRequest(new { message = "Task is not part of this project." });
        if (!CanManage(project) && request.AssignedToId is not null) return Forbid();
        if (!await ValidAssignee(request.AssignedToId))
            return BadRequest(new { message = "Choose an active developer in your team." });

        var bug = new ProjectBug
        {
            ProjectId = projectId, TaskId = request.TaskId,
            Title = request.Title.Trim(), Description = request.Description?.Trim(),
            Severity = request.Severity, AssignedToId = request.AssignedToId,
            ReportedById = LeadAccess.UserId(User)
        };
        db.ProjectBugs.Add(bug);
        project.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Audit("Create", "ProjectBugs", bug.Id);
        await db.SaveChangesAsync();
        return Created("/api/projects/" + projectId + "/bugs", new { bug.Id });
    }

    [Authorize(Roles = "Admin,Manager")]
    [HttpPut("bugs/{bugId:long}")]
    public async Task<IActionResult> UpdateBug(long projectId, long bugId, UpdateProjectBugRequest request)
    {
        var project = await VisibleProjects().Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == projectId);
        if (project is null) return NotFound();
        if (!CanManage(project)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length < 2 ||
            !Severities.Contains(request.Severity) || !BugStatuses.Contains(request.Status))
            return BadRequest(new { message = "Enter a valid bug title, severity and status." });
        if (request.TaskId.HasValue &&
            !await db.DevelopmentTasks.AnyAsync(t => t.Id == request.TaskId && t.ProjectId == projectId))
            return BadRequest(new { message = "Task is not part of this project." });
        if (!await ValidAssignee(request.AssignedToId))
            return BadRequest(new { message = "Choose an active developer in your team." });
        var bug = await db.ProjectBugs.FirstOrDefaultAsync(b => b.Id == bugId && b.ProjectId == projectId);
        if (bug is null) return NotFound();
        bug.Title = request.Title.Trim();
        bug.Description = request.Description?.Trim();
        bug.Severity = request.Severity;
        bug.Status = request.Status;
        bug.TaskId = request.TaskId;
        bug.AssignedToId = request.AssignedToId;
        bug.ClosedAtUtc = bug.Status == "Closed" ? DateTime.UtcNow : null;
        bug.UpdatedAtUtc = DateTime.UtcNow;
        project.UpdatedAtUtc = bug.UpdatedAtUtc;
        Audit("Update", "ProjectBugs", bug.Id);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("bugs/{bugId:long}/status")]
    public async Task<IActionResult> ChangeBugStatus(long projectId, long bugId, ChangeProjectBugStatusRequest request)
    {
        if (!BugStatuses.Contains(request.Status))
            return BadRequest(new { message = "Invalid bug status." });
        var project = await VisibleProjects().Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == projectId);
        if (project is null) return NotFound();
        var bug = await db.ProjectBugs.FirstOrDefaultAsync(b => b.Id == bugId && b.ProjectId == projectId);
        if (bug is null) return NotFound();
        if (!CanManage(project))
        {
            if (bug.AssignedToId != LeadAccess.UserId(User)) return Forbid();
            if (bug.Status == "Closed" || project.Status == "Completed") return Forbid();
            if (request.Status is not ("InProgress" or "Resolved")) return Forbid();
        }
        if (bug.Status == request.Status) return NoContent();
        bug.Status = request.Status;
        bug.ClosedAtUtc = request.Status == "Closed" ? DateTime.UtcNow : null;
        bug.UpdatedAtUtc = DateTime.UtcNow;
        project.UpdatedAtUtc = bug.UpdatedAtUtc;
        Audit("Status", "ProjectBugs", bug.Id);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("worklogs")]
    public async Task<IActionResult> Worklogs(long projectId, DateOnly? from = null, DateOnly? to = null,
        int page = 1, int pageSize = 50)
    {
        if (page < 1 || pageSize is < 1 or > 200 || (long)(page - 1) * pageSize > int.MaxValue)
            return BadRequest(new { message = "Invalid pagination." });
        var project = await VisibleProjects().Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == projectId);
        if (project is null) return NotFound();
        if (from > to) return BadRequest(new { message = "Invalid date range." });
        var logs = db.DeveloperWorklogs.AsNoTracking().Where(w => w.ProjectId == projectId);
        if (!CanManage(project)) logs = logs.Where(w => w.UserId == LeadAccess.UserId(User));
        if (from.HasValue) logs = logs.Where(w => w.WorkDate >= from.Value);
        if (to.HasValue) logs = logs.Where(w => w.WorkDate <= to.Value);
        var total = await logs.CountAsync();
        var totalHours = await logs.SumAsync(w => (decimal?)w.Hours) ?? 0m;
        var items = await logs.OrderByDescending(w => w.WorkDate).ThenByDescending(w => w.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(w => new
            {
                w.Id, w.TaskId, TaskTitle = w.Task == null ? null : w.Task.Title,
                w.UserId, Developer = w.User.FullName, w.WorkDate, w.Hours,
                w.Description, w.CreatedAtUtc
            }).ToListAsync();
        return Ok(new { total, page, pageSize, totalHours, items });
    }

    [HttpPost("worklogs")]
    public async Task<IActionResult> AddWorklog(long projectId, CreateDeveloperWorklogRequest request)
    {
        var project = await VisibleProjects().FirstOrDefaultAsync(p => p.Id == projectId);
        if (project is null) return NotFound();
        if (project.Status == "Completed")
            return BadRequest(new { message = "Reopen the project before adding worklogs." });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.WorkDate > today || request.WorkDate < today.AddDays(-30))
            return BadRequest(new { message = "Work date must be within the last 30 days." });
        if (request.Hours < .25m || request.Hours > 24m || decimal.Round(request.Hours, 2) != request.Hours)
            return BadRequest(new { message = "Hours must be between 0.25 and 24, with at most 2 decimals." });
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length < 2)
            return BadRequest(new { message = "Describe the work completed." });
        var id = LeadAccess.UserId(User);
        if (request.TaskId.HasValue && !await db.DevelopmentTasks.AnyAsync(t =>
            t.Id == request.TaskId && t.ProjectId == projectId &&
            (t.AssignedToId == id || project.OwnerId == id)))
            return BadRequest(new { message = "Select a task assigned to you or log general project work." });
        await using var transaction =
            await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var existingHours = await db.DeveloperWorklogs
            .Where(w => w.UserId == id && w.WorkDate == request.WorkDate)
            .SumAsync(w => (decimal?)w.Hours) ?? 0m;
        if (existingHours + request.Hours > 24m)
            return BadRequest(new { message = "Daily logged time cannot exceed 24 hours." });

        var log = new DeveloperWorklog
        {
            ProjectId = projectId, TaskId = request.TaskId, UserId = id,
            WorkDate = request.WorkDate, Hours = request.Hours,
            Description = request.Description.Trim()
        };
        db.DeveloperWorklogs.Add(log);
        await db.SaveChangesAsync();
        Audit("Create", "DeveloperWorklogs", log.Id);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Created("/api/projects/" + projectId + "/worklogs", new { log.Id });
    }
}
