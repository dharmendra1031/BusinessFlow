using System.Data;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Services;

public sealed class OrganizationService(AppDbContext db)
{
    private static string ValidName(string name)
    {
        name = name.Trim();
        if (name.Length is < 2 or > 150)
            throw new InvalidOperationException("Name must contain 2 to 150 characters.");
        return name;
    }

    private void Audit(string action, string table, long id, long actorId) =>
        db.AuditLogs.Add(new AuditLog { Action = action, TableName = table, RecordId = id, UserId = actorId });

    public async Task<long> SaveDepartmentAsync(long? id, string name, bool isActive, long actorId,
        CancellationToken ct = default)
    {
        name = ValidName(name);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var department = id.HasValue ? await db.Departments.FindAsync([id.Value], ct)
            ?? throw new KeyNotFoundException("Department not found.") : new Department();
        if (await db.Departments.AnyAsync(x => x.Name == name && x.Id != department.Id, ct))
            throw new InvalidOperationException("A department with this name already exists.");
        if (!isActive && id.HasValue &&
            (await db.Teams.AnyAsync(x => x.DepartmentId == id && x.IsActive, ct) ||
             await db.Users.AnyAsync(x => x.DepartmentId == id && x.IsActive, ct)))
            throw new InvalidOperationException("Transfer or deactivate active employees and teams first.");
        department.Name = name;
        department.IsActive = isActive;
        if (!id.HasValue) db.Departments.Add(department);
        await db.SaveChangesAsync(ct);
        Audit(id.HasValue ? "Update" : "Create", "Departments", department.Id, actorId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return department.Id;
    }

    public async Task<long> SaveTeamAsync(long? id, string name, long departmentId, bool isActive, long actorId,
        CancellationToken ct = default)
    {
        name = ValidName(name);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await db.Departments.AnyAsync(x => x.Id == departmentId && (x.IsActive || !isActive), ct))
            throw new InvalidOperationException("Choose an active department.");
        var team = id.HasValue ? await db.Teams.FindAsync([id.Value], ct)
            ?? throw new KeyNotFoundException("Team not found.") : new Team();
        if (await db.Teams.AnyAsync(x => x.DepartmentId == departmentId && x.Name == name && x.Id != team.Id, ct))
            throw new InvalidOperationException("A team with this name already exists in this department.");
        if (id.HasValue && (!isActive || team.DepartmentId != departmentId) &&
            await db.TeamMemberships.AnyAsync(x => x.TeamId == id, ct))
            throw new InvalidOperationException("Transfer or remove team memberships first.");
        team.Name = name;
        team.DepartmentId = departmentId;
        team.IsActive = isActive;
        if (!id.HasValue) db.Teams.Add(team);
        await db.SaveChangesAsync(ct);
        Audit(id.HasValue ? "Update" : "Create", "Teams", team.Id, actorId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return team.Id;
    }

    public async Task TransferAsync(long userId, long? departmentId, long[] teamIds, long actorId,
        CancellationToken ct = default)
    {
        var distinctTeams = teamIds.Distinct().ToArray();
        if (distinctTeams.Length > 200)
            throw new InvalidOperationException("Select at most 200 teams.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await db.Users.FindAsync([userId], ct) ?? throw new KeyNotFoundException("Employee not found.");
        if (!user.IsActive) throw new InvalidOperationException("Activate the employee before changing membership.");
        if (departmentId.HasValue && !await db.Departments.AnyAsync(x => x.Id == departmentId && x.IsActive, ct))
            throw new InvalidOperationException("Choose an active department.");
        if (await db.Teams.CountAsync(x => distinctTeams.Contains(x.Id) && x.IsActive &&
                x.DepartmentId == departmentId, ct) != distinctTeams.Length)
            throw new InvalidOperationException("All teams must be active and belong to the selected department.");
        var memberships = await db.TeamMemberships.Where(x => x.UserId == userId).ToListAsync(ct);
        db.TeamMemberships.RemoveRange(memberships.Where(x => !distinctTeams.Contains(x.TeamId)));
        foreach (var teamId in distinctTeams.Where(id => memberships.All(x => x.TeamId != id)))
            db.TeamMemberships.Add(new TeamMembership { UserId = userId, TeamId = teamId });
        user.DepartmentId = departmentId;
        Audit("Transfer", "Users", userId, actorId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task ValidateManagerAsync(long userId, long? managerId, CancellationToken ct = default)
    {
        var visited = new HashSet<long> { userId };
        while (managerId.HasValue)
        {
            if (!visited.Add(managerId.Value))
                throw new InvalidOperationException("Reporting relationships cannot contain a cycle.");
            var manager = await db.Users.AsNoTracking().Where(x => x.Id == managerId)
                .Select(x => new { x.ManagerId, x.IsActive }).FirstOrDefaultAsync(ct);
            if (manager is null || !manager.IsActive)
                throw new InvalidOperationException("Choose an active reporting manager.");
            managerId = manager.ManagerId;
        }
    }

    public async Task SetManagerAsync(long userId, long? managerId, long actorId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await db.Users.FindAsync([userId], ct) ?? throw new KeyNotFoundException("Employee not found.");
        await ValidateManagerAsync(userId, managerId, ct);
        user.ManagerId = managerId;
        Audit("Manager", "Users", userId, actorId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
