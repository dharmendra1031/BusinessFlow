using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects")]
public sealed class ProjectsController(AppDbContext db, RecordAccessService records) : ControllerBase
{
    private string Action => RolePermissionService.ForRequest(Request) ?? "projects.view";
    private static readonly string[] ProjectStatuses = ["Planning", "InProgress", "OnHold", "Completed"];
    private static readonly string[] TaskStatuses = ["Todo", "InProgress", "Blocked", "Done"];
    private static readonly string[] Priorities = ["Low", "Medium", "High", "Urgent"];

    private Task<IQueryable<DevelopmentProject>> VisibleProjects() => records.ProjectsAsync(User, Action);

    private async Task<bool> CanManage(DevelopmentProject project) =>
        await (await records.ManagedProjectsAsync(User, Action)).AnyAsync(x => x.Id == project.Id);

    private async Task<bool> ValidAssignee(long? id) => id is null ||
        await (await records.EmployeesAsync(User, Action)).AnyAsync(x => x.Id == id && x.IsActive);

    private static bool InvalidDates(DateOnly? start, DateOnly? due) =>
        start.HasValue && due.HasValue && due.Value < start.Value;

    private void Audit(string action, string table, long id) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = LeadAccess.UserId(User), Action = action,
            TableName = table, RecordId = id
        });

    [HttpGet]
    [RequirePermission("projects.view")]
    public async Task<IActionResult> List(string? status = null, int page = 1, int pageSize = 50)
    {
        if (page < 1 || pageSize is < 1 or > 200 || (long)(page - 1) * pageSize > int.MaxValue)
            return BadRequest(new { message = "Invalid pagination." });
        if (status is not null && !ProjectStatuses.Contains(status))
            return BadRequest(new { message = "Invalid project status." });
        var projects = (await VisibleProjects()).AsNoTracking();
        if (status is not null) projects = projects.Where(p => p.Status == status);
        var total = await projects.CountAsync();
        var items = await projects.OrderByDescending(p => p.UpdatedAtUtc).ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                p.Id, p.LeadId, p.CreatedById, p.Title, p.Status, p.StartDate, p.DueDate,
                p.OwnerId, OwnerName = p.Owner.FullName, Customer = p.Lead.Name,
                p.CreatedAtUtc, p.UpdatedAtUtc,
                TaskCount = p.Tasks.Count, DoneCount = p.Tasks.Count(t => t.Status == "Done")
            }).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("eligible-leads")]
    [RequirePermission("projects.create")]
    public async Task<IActionResult> EligibleLeads(string? search = null, int page = 1, int pageSize = 50)
    {
        if (page < 1 || pageSize is < 1 or > 200 || (long)(page - 1) * pageSize > int.MaxValue || search?.Length > 150)
            return BadRequest(new { message = "Invalid eligible lead filters or pagination." });
        var query = (await records.LeadsAsync(User, Action)).AsNoTracking()
            .Where(l => l.StatusId == 5 &&
                !db.DevelopmentProjects.Any(p => p.LeadId == l.Id));
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(l => l.Name.Contains(search.Trim()) || l.CustomerCode!.Contains(search.Trim()));
        var total = await query.CountAsync();
        var items = await query.OrderBy(l => l.Name).ThenBy(l => l.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new { l.Id, l.Name, l.CustomerCode, l.CompanyName })
            .ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id:long}")]
    [RequirePermission("projects.view")]
    public async Task<IActionResult> Get(long id)
    {
        var project = await (await VisibleProjects()).AsNoTracking().Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id, p.LeadId, p.CreatedById, p.Title, p.Description, p.Status, p.StartDate, p.DueDate,
                p.OwnerId, OwnerName = p.Owner.FullName, Customer = p.Lead.Name,
                p.CreatedAtUtc, p.UpdatedAtUtc,
                Tasks = p.Tasks.OrderBy(t => t.Id).Select(t => new
                {
                    t.Id, t.Title, t.Description, t.Status, t.Priority,
                    t.AssignedToId, AssignedTo = t.AssignedTo == null ? null : t.AssignedTo.FullName,
                    t.MilestoneId, t.SprintId, t.DueDate, t.CompletedAtUtc
                }).ToList()
            }).FirstOrDefaultAsync();
        return project is null ? NotFound() : Ok(project);
    }

    [HttpPost]
    [RequirePermission("projects.create")]
    public async Task<IActionResult> Create(CreateProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length < 2)
            return BadRequest(new { message = "Enter a project title of at least 2 characters." });
        if (InvalidDates(request.StartDate, request.DueDate))
            return BadRequest(new { message = "Due date must be on or after the start date." });
        if (!await ValidAssignee(request.OwnerId))
            return BadRequest(new { message = "Choose an active owner from your team." });

        var lead = await (await records.LeadsAsync(User, Action)).AsNoTracking()
            .Where(l => l.Id == request.LeadId).Select(l => new { l.Id, l.StatusId })
            .FirstOrDefaultAsync();
        if (lead is null) return NotFound();
        if (lead.StatusId != 5)
            return BadRequest(new { message = "Only Won leads can become development projects." });
        if (await db.DevelopmentProjects.AnyAsync(p => p.LeadId == request.LeadId))
            return Conflict(new { message = "A project already exists for this lead." });

        var project = new DevelopmentProject
        {
            LeadId = request.LeadId, Title = request.Title.Trim(),
            Description = request.Description?.Trim(), OwnerId = request.OwnerId,
            StartDate = request.StartDate, DueDate = request.DueDate,
            CreatedById = LeadAccess.UserId(User)
        };
        db.DevelopmentProjects.Add(project);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict(new { message = "A project already exists for this lead." });
        }
        Audit("Create", "DevelopmentProjects", project.Id);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = project.Id }, new { project.Id });
    }

    [HttpPut("{id:long}")]
    [RequirePermission("projects.edit")]
    public async Task<IActionResult> Update(long id, UpdateProjectRequest request)
    {
        var project = await (await VisibleProjects()).Include(p => p.Owner).Include(p => p.Tasks)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (project is null) return NotFound();
        if (!await CanManage(project)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length < 2 ||
            !ProjectStatuses.Contains(request.Status))
            return BadRequest(new { message = "Enter a valid project title and status." });
        if (InvalidDates(request.StartDate, request.DueDate))
            return BadRequest(new { message = "Due date must be on or after the start date." });
        if (!await ValidAssignee(request.OwnerId))
            return BadRequest(new { message = "Choose an active owner from your team." });
        if (request.Status == "Completed" &&
            (project.Tasks.Count == 0 || project.Tasks.Any(t => t.Status != "Done")))
            return BadRequest(new { message = "Complete all tasks before completing the project." });
        if (request.Status == "Completed" &&
            await db.ProjectBugs.AnyAsync(b => b.ProjectId == id && b.Status != "Closed"))
            return BadRequest(new { message = "Close all reported bugs before completing the project." });
        if (request.Status == "Completed" &&
            (await db.ProjectMilestones.AnyAsync(m => m.ProjectId == id && m.Status != "Completed") ||
             await db.ProjectSprints.AnyAsync(sp => sp.ProjectId == id && sp.Status != "Completed")))
            return BadRequest(new { message = "Complete project milestones and sprints first." });
        if (request.DueDate.HasValue &&
            (await db.ProjectMilestones.AnyAsync(m => m.ProjectId == id && m.DueDate > request.DueDate) ||
             await db.ProjectSprints.AnyAsync(sp => sp.ProjectId == id && sp.DueDate > request.DueDate)))
            return BadRequest(new { message = "Project deadline cannot precede existing planning deadlines." });
        if (request.StartDate.HasValue &&
            await db.ProjectSprints.AnyAsync(sp => sp.ProjectId == id && sp.StartDate < request.StartDate))
            return BadRequest(new { message = "Project start date cannot follow a sprint start." });

        project.Title = request.Title.Trim();
        project.Description = request.Description?.Trim();
        project.Status = request.Status;
        project.OwnerId = request.OwnerId;
        project.StartDate = request.StartDate;
        project.DueDate = request.DueDate;
        project.UpdatedAtUtc = DateTime.UtcNow;
        Audit("Update", "DevelopmentProjects", project.Id);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:long}/tasks")]
    [RequirePermission("tasks.create")]
    public async Task<IActionResult> AddTask(long id, CreateDevelopmentTaskRequest request)
    {
        var project = await (await VisibleProjects()).Include(p => p.Owner).FirstOrDefaultAsync(p => p.Id == id);
        if (project is null) return NotFound();
        if (!await CanManage(project)) return Forbid();
        if (project.Status == "Completed")
            return BadRequest(new { message = "Reopen the project before adding tasks." });
        var error = await TaskError(request.Title, request.Priority, request.AssignedToId,
            request.DueDate, project.DueDate, id, request.MilestoneId, request.SprintId);
        if (error is not null) return BadRequest(new { message = error });

        var task = new DevelopmentTask
        {
            ProjectId = id, Title = request.Title.Trim(), Description = request.Description?.Trim(),
            Priority = request.Priority, AssignedToId = request.AssignedToId, DueDate = request.DueDate,
            MilestoneId = request.MilestoneId, SprintId = request.SprintId
        };
        db.DevelopmentTasks.Add(task);
        project.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Audit("Create", "DevelopmentTasks", task.Id);
        await db.SaveChangesAsync();
        return Created("/api/projects/" + id, new { task.Id });
    }

    [HttpPut("{id:long}/tasks/{taskId:long}")]
    [RequirePermission("tasks.edit")]
    public async Task<IActionResult> UpdateTask(long id, long taskId, UpdateDevelopmentTaskRequest request)
    {
        var project = await (await VisibleProjects()).Include(p => p.Owner).FirstOrDefaultAsync(p => p.Id == id);
        if (project is null) return NotFound();
        if (!await CanManage(project)) return Forbid();
        if (project.Status == "Completed")
            return BadRequest(new { message = "Reopen the project before editing tasks." });
        var task = await db.DevelopmentTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.ProjectId == id);
        if (task is null) return NotFound();
        var error = await TaskError(request.Title, request.Priority, request.AssignedToId,
            request.DueDate, project.DueDate, id, request.MilestoneId, request.SprintId);
        if (error is not null) return BadRequest(new { message = error });

        task.Title = request.Title.Trim();
        task.Description = request.Description?.Trim();
        task.Priority = request.Priority;
        task.AssignedToId = request.AssignedToId;
        task.DueDate = request.DueDate;
        task.MilestoneId = request.MilestoneId;
        task.SprintId = request.SprintId;
        task.UpdatedAtUtc = DateTime.UtcNow;
        project.UpdatedAtUtc = task.UpdatedAtUtc;
        Audit("Update", "DevelopmentTasks", taskId);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id:long}/tasks/{taskId:long}/status")]
    [RequirePermission("tasks.status")]
    public async Task<IActionResult> ChangeTaskStatus(long id, long taskId, UpdateDevelopmentTaskStatusRequest request)
    {
        if (!TaskStatuses.Contains(request.Status))
            return BadRequest(new { message = "Invalid task status." });
        var project = await (await VisibleProjects()).Include(p => p.Owner).FirstOrDefaultAsync(p => p.Id == id);
        if (project is null) return NotFound();
        if (project.Status == "Completed")
            return BadRequest(new { message = "Reopen the project before updating tasks." });
        var task = await db.DevelopmentTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.ProjectId == id);
        if (task is null) return NotFound();
        if (!await CanManage(project) && task.AssignedToId != LeadAccess.UserId(User)) return Forbid();
        if (task.Status == request.Status) return NoContent();

        task.Status = request.Status;
        task.CompletedAtUtc = request.Status == "Done" ? DateTime.UtcNow : null;
        task.UpdatedAtUtc = DateTime.UtcNow;
        project.UpdatedAtUtc = task.UpdatedAtUtc;
        Audit("Status", "DevelopmentTasks", taskId);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<string?> TaskError(string title, string priority, long? assigneeId,
        DateOnly? dueDate, DateOnly? projectDueDate, long projectId, long? milestoneId, long? sprintId)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length < 2)
            return "Task title must have at least two characters.";
        if (!Priorities.Contains(priority)) return "Invalid task priority.";
        if (!await ValidAssignee(assigneeId)) return "Select an active team member.";
        if (projectDueDate.HasValue && dueDate > projectDueDate)
            return "Task deadline cannot be after the project deadline.";
        if (milestoneId.HasValue)
        {
            var milestone = await db.ProjectMilestones.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == milestoneId && m.ProjectId == projectId);
            if (milestone is null || milestone.Status == "Completed")
                return "Choose an open milestone in this project.";
            if (dueDate.HasValue && milestone.DueDate.HasValue && dueDate > milestone.DueDate)
                return "Task deadline cannot exceed its milestone deadline.";
        }
        if (sprintId.HasValue)
        {
            var sprint = await db.ProjectSprints.AsNoTracking()
                .FirstOrDefaultAsync(sp => sp.Id == sprintId && sp.ProjectId == projectId);
            if (sprint is null || sprint.Status == "Completed")
                return "Choose an open sprint in this project.";
            if (dueDate.HasValue && dueDate > sprint.DueDate)
                return "Task deadline cannot exceed its sprint deadline.";
        }
        return null;
    }
}
