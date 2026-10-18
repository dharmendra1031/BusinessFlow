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
[Route("api/projects")]
public sealed class ProjectsController(AppDbContext db) : ControllerBase
{
    private static readonly string[] ProjectStatuses = ["Planned", "InProgress", "OnHold", "Completed", "Cancelled"];
    private static readonly string[] TaskStatuses = ["Todo", "InProgress", "Blocked", "Done"];

    private IQueryable<DevelopmentProject> VisibleProjects()
    {
        var projects = db.DevelopmentProjects.AsNoTracking();
        if (User.IsInRole("Admin")) return projects;

        var userId = LeadAccess.UserId(User);
        if (User.IsInRole("Manager"))
            return projects.Where(p => p.OwnerId == userId || p.Lead.CreatedById == userId ||
                p.Lead.AssignedToId == userId ||
                (p.Lead.AssignedTo != null && p.Lead.AssignedTo.ManagerId == userId));

        return projects.Where(p => p.OwnerId == userId ||
            p.Tasks.Any(t => t.AssignedToId == userId));
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var items = await VisibleProjects().OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new
            {
                p.Id, p.LeadId, LeadName = p.Lead.Name, p.Name, p.Description, p.Status,
                p.OwnerId, OwnerName = p.Owner.FullName, p.StartDate, p.DueDate, p.CreatedAtUtc,
                TaskCount = p.Tasks.Count,
                CompletedTasks = p.Tasks.Count(t => t.Status == "Done")
            }).Take(200).ToListAsync();
        return Ok(items);
    }

    [HttpGet("eligible-leads")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> EligibleLeads()
    {
        var leads = LeadAccess.VisibleTo(db.Leads.AsNoTracking(), User);
        return Ok(await leads.Where(l => l.Status.Name == "Won")
            .OrderBy(l => l.Name).Select(l => new { l.Id, l.Name, l.CustomerCode })
            .Take(200).ToListAsync());
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id)
    {
        var project = await VisibleProjects().Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id, p.LeadId, LeadName = p.Lead.Name, p.Name, p.Description, p.Status,
                p.OwnerId, OwnerName = p.Owner.FullName, p.StartDate, p.DueDate, p.CreatedAtUtc,
                Tasks = p.Tasks.OrderBy(t => t.Id).Select(t => new
                {
                    t.Id, t.Title, t.Description, t.Status, t.AssignedToId,
                    AssignedToName = t.AssignedTo != null ? t.AssignedTo.FullName : null,
                    t.DueDate, t.CreatedAtUtc
                }).ToList()
            }).FirstOrDefaultAsync();
        return project is null ? NotFound() : Ok(project);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Create(CreateProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 2)
            return BadRequest(new { message = "Project name must contain at least two characters." });
        if (request.StartDate.HasValue && request.DueDate.HasValue && request.StartDate > request.DueDate)
            return BadRequest(new { message = "Due date must not be before start date." });
        var lead = await LeadAccess.VisibleTo(db.Leads, User)
            .FirstOrDefaultAsync(l => l.Id == request.LeadId);
        if (lead is null) return NotFound();
        if (!await db.LeadStatuses.AnyAsync(s => s.Id == lead.StatusId && s.Name == "Won"))
            return BadRequest(new { message = "Only won leads can be converted to development projects." });
        if (!await CanAssign(request.OwnerId))
            return BadRequest(new { message = "Select an active project owner from your team." });

        var project = new DevelopmentProject
        {
            LeadId = lead.Id, Name = request.Name.Trim(),
            Description = request.Description?.Trim(), OwnerId = request.OwnerId,
            StartDate = request.StartDate?.Date, DueDate = request.DueDate?.Date,
            CreatedById = LeadAccess.UserId(User)
        };
        db.DevelopmentProjects.Add(project);
        await db.SaveChangesAsync();
        db.AuditLogs.Add(new AuditLog
        {
            UserId = project.CreatedById, Action = "Create",
            TableName = "DevelopmentProjects", RecordId = project.Id
        });
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = project.Id }, new { project.Id });
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Update(long id, UpdateProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 2)
            return BadRequest(new { message = "Project name must contain at least two characters." });
        if (!ProjectStatuses.Contains(request.Status))
            return BadRequest(new { message = "Invalid project status." });
        if (request.StartDate.HasValue && request.DueDate.HasValue && request.StartDate > request.DueDate)
            return BadRequest(new { message = "Due date must not be before start date." });
        if (!await CanAssign(request.OwnerId))
            return BadRequest(new { message = "Select an active project owner from your team." });

        var project = await EditableProject(id);
        if (project is null) return NotFound();
        project.Name = request.Name.Trim();
        project.Description = request.Description?.Trim();
        project.Status = request.Status;
        project.OwnerId = request.OwnerId;
        project.StartDate = request.StartDate?.Date;
        project.DueDate = request.DueDate?.Date;
        project.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = "Update",
            TableName = "DevelopmentProjects", RecordId = project.Id
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:long}/tasks")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> CreateTask(long id, CreateDevelopmentTaskRequest request)
    {
        var project = await EditableProject(id);
        if (project is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length < 2)
            return BadRequest(new { message = "Task title must contain at least two characters." });
        if (request.AssignedToId.HasValue && !await CanAssign(request.AssignedToId.Value))
            return BadRequest(new { message = "Select an active team member." });

        var task = new DevelopmentTask
        {
            ProjectId = id, Title = request.Title.Trim(), Description = request.Description?.Trim(),
            AssignedToId = request.AssignedToId, DueDate = request.DueDate?.Date,
            CreatedById = LeadAccess.UserId(User)
        };
        db.DevelopmentTasks.Add(task);
        await db.SaveChangesAsync();
        db.AuditLogs.Add(new AuditLog
        {
            UserId = task.CreatedById, Action = "Create", TableName = "DevelopmentTasks", RecordId = task.Id
        });
        await db.SaveChangesAsync();
        return Created("/api/projects/" + id, new { task.Id });
    }

    [HttpPatch("{id:long}/tasks/{taskId:long}/status")]
    public async Task<IActionResult> UpdateTaskStatus(long id, long taskId, UpdateDevelopmentTaskRequest request)
    {
        if (!TaskStatuses.Contains(request.Status))
            return BadRequest(new { message = "Invalid task status." });
        if (!await VisibleProjects().AnyAsync(p => p.Id == id)) return NotFound();
        var task = await db.DevelopmentTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.ProjectId == id);
        if (task is null) return NotFound();
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") &&
            task.AssignedToId != LeadAccess.UserId(User))
            return Forbid();
        task.Status = request.Status;
        task.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = "Update",
            TableName = "DevelopmentTasks", RecordId = task.Id, ChangedFields = "Status"
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<DevelopmentProject?> EditableProject(long id)
    {
        var projectId = await VisibleProjects().Where(p => p.Id == id)
            .Select(p => (long?)p.Id).FirstOrDefaultAsync();
        return projectId.HasValue
            ? await db.DevelopmentProjects.FirstOrDefaultAsync(p => p.Id == projectId.Value)
            : null;
    }

    private async Task<bool> CanAssign(long userId)
    {
        var currentId = LeadAccess.UserId(User);
        return await db.Users.AnyAsync(u => u.Id == userId && u.IsActive &&
            (User.IsInRole("Admin") || u.Id == currentId || u.ManagerId == currentId));
    }
}
