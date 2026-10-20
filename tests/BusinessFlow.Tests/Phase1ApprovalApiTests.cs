using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1ApprovalApiTests
{
    [Fact]
    public async Task Terminal_status_requires_approval_or_a_reasoned_admin_override()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var employee = await host.ClientAsync("Employee");
        using var admin = await host.ClientAsync("Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsJsonAsync("/api/leads",
            new { name = "Bypass", phone = "1234567890", sourceId = 1, statusId = 5 })).StatusCode);
        var created = await employee.PostAsJsonAsync("/api/leads", new { name = "Customer", phone = "1234567890", sourceId = 1 });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var path = $"/api/leads/{id}";
        var version = (await employee.GetFromJsonAsync<JsonElement>(path)).GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.PatchAsJsonAsync(path + "/status", new { statusId = 5, rowVersion = version })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await employee.PostAsJsonAsync($"/api/lead-approvals/leads/{id}", new { statusId = 5, rowVersion = version })).StatusCode);
        var unchanged = await employee.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(1, unchanged.GetProperty("statusId").GetInt32());
        var pending = await admin.GetFromJsonAsync<JsonElement>("/api/lead-approvals");
        var approvalId = pending.GetProperty("items")[0].GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/lead-approvals/{approvalId}/decision", new { approve = true, comment = "Reviewed" })).StatusCode);
        var current = await admin.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(5, current.GetProperty("statusId").GetInt32());
        version = current.GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync(path + "/status", new { statusId = 6, rowVersion = version })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync(path + "/status", new { statusId = 6, rowVersion = version, overrideReason = "Client withdrew after approval" })).StatusCode);
        Assert.Equal(6, (await admin.GetFromJsonAsync<JsonElement>(path)).GetProperty("statusId").GetInt32());
    }
}
