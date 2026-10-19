using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:long}/planning")]
public sealed class ProjectPlanningController(AppDbContext db) : ControllerBase
{
    private static readonly string[] Statuses = ["Planned", "InProgress", "Completed"];

    private IQueryable<DevelopmentProject> Visible()
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

    private bool CanManage(DevelopmentProject p) =>
        User.IsInRole("Admin") || (User.IsInRole("Manager") &&
            (p.OwnerId == LeadAccess.UserId(User) ||
             p.CreatedById == LeadAccess.UserId(User) ||
             p.Owner.ManagerId == LeadAccess.UserId(User)));

    private async Task<DevelopmentProject?> Project(long id) =>
        await Visible().Include(p => p.Owner).FirstOrDefaultAsync(p => p.Id == id);

    private void Audit(string action, string table, long id) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = action,
            TableName = table, RecordId = id
        });

    [HttpGet]
    public async Task<IActionResult> Get(long projectId)
    {
        if (!await Visible().AnyAsync(p => p.Id == projectId)) return NotFound();
        var milestones = await db.ProjectMilestones.AsNoTracking()
            .Where(m => m.ProjectId == projectId).OrderBy(m => m.DueDate).ThenBy(m => m.Id)
            .Select(m => new
            {
                m.Id, m.Title, m.Description, m.Status, m.DueDate,
                TaskCount = db.DevelopmentTasks.Count(t => t.MilestoneId == m.Id),
                DoneCount = db.DevelopmentTasks.Count(t => t.MilestoneId == m.Id && t.Status == "Done")
            }).ToListAsync();
        var sprints = await db.ProjectSprints.AsNoTracking()
            .Where(s => s.ProjectId == projectId).OrderBy(s => s.StartDate).ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Id, s.Title, s.Goal, s.Status, s.StartDate, s.DueDate,
                TaskCount = db.DevelopmentTasks.Count(t => t.SprintId == s.Id),
                DoneCount = db.DevelopmentTasks.Count(t => t.SprintId == s.Id && t.Status == "Done")
            }).ToListAsync();
        return Ok(new { milestones, sprints });
    }

    [HttpPost("milestones")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> AddMilestone(long projectId, SaveMilestoneRequest request)
    {
        var project = await Project(projectId);
        if (project is null) return NotFound();
        if (!CanManage(project)) return Forbid();
        if (project.Status == "Completed") return BadRequest(new { message = "Reopen the project first." });
        if (InvalidMilestone(request, project) || request.Status == "Completed")
            return BadRequest(new { message = "Invalid milestone details or initial status." });
        var milestone = new ProjectMilestone
        {
            ProjectId = projectId, Title = request.Title.Trim(),
            Description = request.Description?.Trim(), Status = request.Status,
            DueDate = request.DueDate
        };
        db.ProjectMilestones.Add(milestone);
        project.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Audit("Create", "ProjectMilestones", milestone.Id);
        await db.SaveChangesAsync();
        return Created("/api/projects/" + projectId + "/planning", new { milestone.Id });
    }

    [HttpPut("milestones/{id:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> UpdateMilestone(long projectId, long id, SaveMilestoneRequest request)
    {
        var project = await Project(projectId);
        if (project is null) return NotFound();
        if (!CanManage(project)) return Forbid();
        if (project.Status == "Completed") return BadRequest(new { message = "Reopen the project first." });
        var milestone = await db.ProjectMilestones.FirstOrDefaultAsync(m => m.Id == id && m.ProjectId == projectId);
        if (milestone is null) return NotFound();
        if (InvalidMilestone(request, project))
            return BadRequest(new { message = "Invalid milestone title, status or deadline." });
        if (request.Status == "Completed" &&
            !await HasOnlyDoneTasks(t => t.MilestoneId == id))
            return BadRequest(new { message = "Finish the milestone's tasks before completing it." });
        milestone.Title = request.Title.Trim();
        milestone.Description = request.Description?.Trim();
        milestone.Status = request.Status;
        milestone.DueDate = request.DueDate;
        project.UpdatedAtUtc = DateTime.UtcNow;
        Audit("Update", "ProjectMilestones", id);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("sprints")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> AddSprint(long projectId, SaveSprintRequest request)
    {
        var project = await Project(projectId);
        if (project is null) return NotFound();
        if (!CanManage(project)) return Forbid();
        if (project.Status == "Completed") return BadRequest(new { message = "Reopen the project first." });
        if (InvalidSprint(request, project) || request.Status == "Completed")
            return BadRequest(new { message = "Invalid sprint details or initial status." });
        var sprint = new ProjectSprint
        {
            ProjectId = projectId, Title = request.Title.Trim(), Goal = request.Goal?.Trim(),
            Status = request.Status, StartDate = request.StartDate, DueDate = request.DueDate
        };
        db.ProjectSprints.Add(sprint);
        project.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Audit("Create", "ProjectSprints", sprint.Id);
        await db.SaveChangesAsync();
        return Created("/api/projects/" + projectId + "/planning", new { sprint.Id });
    }

    [HttpPut("sprints/{id:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> UpdateSprint(long projectId, long id, SaveSprintRequest request)
    {
        var project = await Project(projectId);
        if (project is null) return NotFound();
        if (!CanManage(project)) return Forbid();
        if (project.Status == "Completed") return BadRequest(new { message = "Reopen the project first." });
        var sprint = await db.ProjectSprints.FirstOrDefaultAsync(s => s.Id == id && s.ProjectId == projectId);
        if (sprint is null) return NotFound();
        if (InvalidSprint(request, project))
            return BadRequest(new { message = "Invalid sprint title, status or dates." });
        if (request.Status == "Completed" &&
            !await HasOnlyDoneTasks(t => t.SprintId == id))
            return BadRequest(new { message = "Finish sprint tasks before completing it." });
        sprint.Title = request.Title.Trim();
        sprint.Goal = request.Goal?.Trim();
        sprint.Status = request.Status;
        sprint.StartDate = request.StartDate;
        sprint.DueDate = request.DueDate;
        project.UpdatedAtUtc = DateTime.UtcNow;
        Audit("Update", "ProjectSprints", id);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<bool> HasOnlyDoneTasks(System.Linq.Expressions.Expression<Func<DevelopmentTask, bool>> filter)
    {
        var tasks = db.DevelopmentTasks.Where(filter);
        return await tasks.AnyAsync() && !await tasks.AnyAsync(t => t.Status != "Done");
    }

    private static bool InvalidMilestone(SaveMilestoneRequest req, DevelopmentProject project) =>
        string.IsNullOrWhiteSpace(req.Title) || req.Title.Trim().Length < 2 ||
        !Statuses.Contains(req.Status) ||
        (project.DueDate.HasValue && req.DueDate > project.DueDate);

    private static bool InvalidSprint(SaveSprintRequest req, DevelopmentProject project) =>
        string.IsNullOrWhiteSpace(req.Title) || req.Title.Trim().Length < 2 ||
        !Statuses.Contains(req.Status) || req.StartDate == default || req.DueDate == default ||
        req.StartDate > req.DueDate ||
        (project.StartDate.HasValue && req.StartDate < project.StartDate.Value) ||
        (project.DueDate.HasValue && req.DueDate > project.DueDate.Value);
}
