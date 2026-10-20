using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1ApprovalTests
{
    [Theory]
    [InlineData(true, 5)]
    [InlineData(false, 1)]
    public async Task Another_scoped_approver_decides_without_changing_status_on_request(bool approve, long expectedStatus)
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var db = fixture.CreateContext();
        var requester = new ApplicationUser { FullName = "Requester" };
        var approver = new ApplicationUser { FullName = "Approver" };
        db.Users.AddRange(requester, approver);
        await db.SaveChangesAsync();
        var lead = new Lead { Name = "Customer", Phone = "1234567890", SourceId = 1, StatusId = 1,
            CreatedById = requester.Id, AssignedToId = requester.Id };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        var service = new LeadApprovalService(db, new RecordAccessService(db, new RolePermissionService(db)), new RolePermissionService(db));
        var person = Phase1PermissionTests.Principal(requester.Id, "Employee");
        var admin = Phase1PermissionTests.Principal(approver.Id, "Admin");
        var approvalId = await service.RequestAsync(lead.Id, 5, Convert.ToBase64String(lead.RowVersion), person);
        Assert.Equal(1, lead.StatusId);
        Assert.Empty(await db.LeadStatusChanges.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestAsync(lead.Id, 6, Convert.ToBase64String(lead.RowVersion), person));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync(approvalId, true, "Self approval", Phase1PermissionTests.Principal(requester.Id, "Admin")));
        await service.DecideAsync(approvalId, approve, "Reviewed", admin);
        Assert.Equal(expectedStatus, lead.StatusId);
        Assert.Equal(approve ? 1 : 0, await db.LeadStatusChanges.CountAsync());
        Assert.Equal(approve ? "Approved" : "Rejected", (await db.LeadStatusApprovals.SingleAsync()).State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync(approvalId, approve, "Again", admin));
    }

    [Fact]
    public async Task Approval_cannot_apply_after_an_intervening_lead_edit()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var db = fixture.CreateContext();
        var person = new ApplicationUser { FullName = "Requester" };
        db.Users.Add(person);
        await db.SaveChangesAsync();
        var lead = new Lead { Name = "Customer", Phone = "1234567890", SourceId = 1, StatusId = 1,
            CreatedById = person.Id, AssignedToId = person.Id };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        var permission = new RolePermissionService(db);
        var service = new LeadApprovalService(db, new RecordAccessService(db, permission), permission);
        var approvalId = await service.RequestAsync(lead.Id, 6, Convert.ToBase64String(lead.RowVersion), Phase1PermissionTests.Principal(person.Id, "Employee"));
        lead.Name = "Updated customer";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.DecideAsync(approvalId, true, null,
            Phase1PermissionTests.Principal(person.Id + 100, "Admin")));
        Assert.Equal(1, lead.StatusId);
        Assert.Empty(await db.LeadStatusChanges.ToListAsync());
    }
}
