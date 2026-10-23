using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase2DuplicateLeadTests
{
    [Fact]
    public async Task Admin_reviews_normalized_matches_without_merging_or_changing_ownership()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(fixture);
        using var admin = await host.ClientAsync("Admin");
        using var employee = await host.ClientAsync("Employee");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.GetAsync("/api/admin/lead-duplicates")).StatusCode);

        var first = await admin.PostAsJsonAsync("/api/leads",
            new { name = "First customer", phone = "+965 123-4567", email = "First@Example.test", sourceId = 1 });
        var second = await admin.PostAsJsonAsync("/api/leads",
            new { name = "Second customer", phone = "+9651234567", email = "other@example.test", sourceId = 1 });
        var third = await admin.PostAsJsonAsync("/api/leads",
            new { name = "Third customer", phone = "9876543210", email = " FIRST@example.test ", sourceId = 1 });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, third.StatusCode);

        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var thirdId = (await third.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        var pending = await admin.GetFromJsonAsync<JsonElement>("/api/admin/lead-duplicates");
        Assert.Equal(2, pending.GetProperty("total").GetInt32());
        var pairs = pending.GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(pairs, pair => pair.GetProperty("firstId").GetInt64() == firstId &&
            pair.GetProperty("secondId").GetInt64() == secondId &&
            pair.GetProperty("matchPhone").GetBoolean());
        Assert.Contains(pairs, pair => pair.GetProperty("firstId").GetInt64() == firstId &&
            pair.GetProperty("secondId").GetInt64() == thirdId &&
            pair.GetProperty("matchEmail").GetBoolean());

        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.PostAsJsonAsync($"/api/admin/lead-duplicates/{firstId}/{secondId}",
                new { decision = "NotDuplicate", comment = "Different customers" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/admin/lead-duplicates/{firstId}/{secondId}",
                new { decision = "Delete", comment = "Delete them" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"/api/admin/lead-duplicates/{firstId}/{secondId}",
                new { decision = "NotDuplicate", comment = "Separate customers with a shared phone" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/admin/lead-duplicates/{firstId}/{secondId}",
                new { decision = "Confirmed", comment = "Cannot overwrite an audit decision" })).StatusCode);

        var remaining = await admin.GetFromJsonAsync<JsonElement>("/api/admin/lead-duplicates?state=Pending");
        Assert.Equal(1, remaining.GetProperty("total").GetInt32());
        var reviewed = await admin.GetFromJsonAsync<JsonElement>("/api/admin/lead-duplicates?state=Reviewed");
        Assert.Equal(1, reviewed.GetProperty("total").GetInt32());
        Assert.Equal("NotDuplicate", reviewed.GetProperty("items")[0].GetProperty("decision").GetString());

        await using var db = fixture.CreateContext();
        Assert.Equal(3, await db.Leads.CountAsync());
        Assert.Equal(1, await db.LeadDuplicateReviews.CountAsync());
        Assert.True(await db.AuditLogs.AnyAsync(x => x.TableName == "LeadDuplicateReviews" && x.RecordId == firstId));
        Assert.True(await db.Leads.AllAsync(x => x.AssignedToId != null && x.StatusId == 1));
    }

    [Fact]
    public async Task Upgrade_preserves_existing_lead_ids_and_does_not_create_reviews()
    {
        await using var fixture = await Phase1TestDatabase.CreateAsync("20261023090000_AddLeadSourceManagement");
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Leads (Name, Phone, SourceId, StatusId, CreatedById, CreatedAtUtc, UpdatedAtUtc)
            VALUES (N'Existing customer', N'987-6543210', 1, 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            """);
        var before = await db.Leads.AsNoTracking().Select(x => x.Id).ToArrayAsync();
        await db.Database.MigrateAsync();
        var after = await db.Leads.AsNoTracking().Select(x => x.Id).ToArrayAsync();
        Assert.Equal(before, after);
        Assert.Empty(await db.LeadDuplicateReviews.ToListAsync());
        Assert.Equal("9876543210", await db.Leads.Select(x => x.NormalizedPhone).SingleAsync());
    }
}
