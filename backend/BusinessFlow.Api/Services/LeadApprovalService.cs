using System.Data;
using System.Security.Claims;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Services;

public sealed class LeadApprovalService(AppDbContext db, RecordAccessService access, RolePermissionService permissions)
{
    private void Audit(string action, long leadId, long actor, string detail) => db.AuditLogs.Add(new AuditLog
    { Action = action, TableName = "LeadApprovals", RecordId = leadId, UserId = actor, ChangedFields = detail });

    public async Task<long> RequestAsync(long leadId, long statusId, string? version, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!await permissions.AllowedAsync(user, "leads.request")) throw new UnauthorizedAccessException();
        if (statusId is not (5 or 6)) throw new InvalidOperationException("Approval requests are only for Won or Lost.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var lead = await (await access.LeadsAsync(user, "leads.request", ct)).SingleOrDefaultAsync(x => x.Id == leadId, ct)
            ?? throw new KeyNotFoundException("Lead not found.");
        if (!ConcurrencyTokens.Apply(db, lead, version)) throw new InvalidOperationException("A valid row version is required.");
        if (lead.StatusId == statusId) throw new InvalidOperationException("The lead already has this status.");
        if (await db.LeadStatusApprovals.AnyAsync(x => x.LeadId == leadId && x.State == "Pending", ct))
            throw new InvalidOperationException("A pending request already exists for this lead.");
        var approval = new LeadStatusApproval { LeadId = leadId, RequestedStatusId = statusId,
            RequestedById = LeadAccess.UserId(user), LeadVersion = lead.RowVersion.ToArray() };
        db.LeadStatusApprovals.Add(approval);
        await db.SaveChangesAsync(ct);
        Audit("Request", leadId, approval.RequestedById, $"Approval {approval.Id}; status {statusId}");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return approval.Id;
    }

    public async Task DecideAsync(long approvalId, bool approve, string? comment, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!await permissions.AllowedAsync(user, "leads.approve")) throw new UnauthorizedAccessException();
        if (comment?.Length > 500) throw new InvalidOperationException("Comment must not exceed 500 characters.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var approval = await db.LeadStatusApprovals.SingleOrDefaultAsync(x => x.Id == approvalId, ct)
            ?? throw new KeyNotFoundException("Approval request not found.");
        var lead = await (await access.LeadsAsync(user, "leads.approve", ct)).SingleOrDefaultAsync(x => x.Id == approval.LeadId, ct)
            ?? throw new KeyNotFoundException("Lead not found.");
        if (approval.State != "Pending") throw new InvalidOperationException("This request has already been decided.");
        var actor = LeadAccess.UserId(user);
        if (actor == approval.RequestedById) throw new InvalidOperationException("A different authorized approver must decide this request.");
        if (approve && !lead.RowVersion.SequenceEqual(approval.LeadVersion)) throw new DbUpdateConcurrencyException();
        if (approve)
        {
            db.LeadStatusChanges.Add(new LeadStatusChange { LeadId = lead.Id, OldStatusId = lead.StatusId,
                NewStatusId = approval.RequestedStatusId, ChangedById = actor });
            lead.StatusId = approval.RequestedStatusId;
            lead.UpdatedAtUtc = DateTime.UtcNow;
            lead.ModifiedByUserId = actor;
        }
        approval.State = approve ? "Approved" : "Rejected";
        approval.DecidedById = actor;
        approval.DecidedAtUtc = DateTime.UtcNow;
        approval.Comment = comment?.Trim();
        Audit(approval.State, lead.Id, actor, $"Approval {approval.Id}; {approval.Comment}");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
