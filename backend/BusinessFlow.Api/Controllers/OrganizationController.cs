using BusinessFlow.Api.Contracts;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/organization")]
public sealed class OrganizationController(AppDbContext db, OrganizationService organization) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(new
    {
        departments = await db.Departments.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct),
        teams = await db.Teams.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct),
        memberships = await db.TeamMemberships.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)
    });

    private async Task<IActionResult> Execute(Func<Task<object>> operation)
    {
        try { return Ok(await operation()); }
        catch (Exception e) when (SqlConflicts.IsRetryable(e))
        { return Conflict(new { message = "Organization changed concurrently. Reload and retry." }); }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
        catch (InvalidOperationException e) { return BadRequest(new { message = e.Message }); }
    }

    [HttpPost("departments")]
    public Task<IActionResult> CreateDepartment(SaveDepartmentRequest request, CancellationToken ct) =>
        SaveDepartment(null, request, ct);

    [HttpPut("departments/{id:long}")]
    public Task<IActionResult> UpdateDepartment(long id, SaveDepartmentRequest request, CancellationToken ct) =>
        SaveDepartment(id, request, ct);

    private Task<IActionResult> SaveDepartment(long? id, SaveDepartmentRequest request, CancellationToken ct) =>
        Execute(async () => new { id = await organization.SaveDepartmentAsync(id, request.Name, request.IsActive, LeadAccess.UserId(User), ct) });

    [HttpPost("teams")]
    public Task<IActionResult> CreateTeam(SaveTeamRequest request, CancellationToken ct) => SaveTeam(null, request, ct);

    [HttpPut("teams/{id:long}")]
    public Task<IActionResult> UpdateTeam(long id, SaveTeamRequest request, CancellationToken ct) => SaveTeam(id, request, ct);

    private Task<IActionResult> SaveTeam(long? id, SaveTeamRequest request, CancellationToken ct) =>
        Execute(async () => new { id = await organization.SaveTeamAsync(id, request.Name, request.DepartmentId, request.IsActive, LeadAccess.UserId(User), ct) });

    [HttpPut("employees/{id:long}/membership")]
    public Task<IActionResult> Transfer(long id, TransferEmployeeRequest request, CancellationToken ct) => Execute(async () =>
    {
        await organization.TransferAsync(id, request.DepartmentId, request.TeamIds, LeadAccess.UserId(User), ct);
        return new { id };
    });

    [HttpPut("employees/{id:long}/manager")]
    public Task<IActionResult> SetManager(long id, SetManagerRequest request, CancellationToken ct) => Execute(async () =>
    {
        await organization.SetManagerAsync(id, request.ManagerId, LeadAccess.UserId(User), ct);
        return new { id };
    });
}
