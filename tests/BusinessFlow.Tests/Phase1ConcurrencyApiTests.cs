using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1ConcurrencyApiTests
{
    [Fact]
    public async Task Project_and_task_writes_reject_stale_versions()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        var ownerId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();
        var leadResponse = await admin.PostAsJsonAsync("/api/leads", new { name = "Customer", phone = "1234567890", sourceId = 1, statusId = 5, overrideReason = "Concurrency test setup" });
        var leadId = (await leadResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var response = await admin.PostAsJsonAsync("/api/projects", new { leadId, title = "Delivery", ownerId });
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var path = $"/api/projects/{id}";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(path, new { title = "Missing", status = "Planning", ownerId })).StatusCode);
        var version = (await admin.GetFromJsonAsync<JsonElement>(path)).GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(path, new { title = "New delivery", status = "Planning", ownerId, rowVersion = version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(path, new { title = "Stale", status = "Planning", ownerId, rowVersion = version })).StatusCode);
        var taskResponse = await admin.PostAsJsonAsync(path + "/tasks", new { title = "Task", priority = "Medium", assignedToId = ownerId });
        var taskId = (await taskResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var project = await admin.GetFromJsonAsync<JsonElement>(path);
        var taskVersion = project.GetProperty("tasks")[0].GetProperty("rowVersion").GetString();
        var taskPath = path + $"/tasks/{taskId}";
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync(taskPath + "/status", new { status = "InProgress", rowVersion = taskVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(taskPath, new { title = "Stale", priority = "Medium", assignedToId = ownerId, rowVersion = taskVersion })).StatusCode);
        var current = await admin.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("Task", current.GetProperty("tasks")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Lead_edits_require_a_current_version_and_stale_writes_do_not_overwrite()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");
        var created = await admin.PostAsJsonAsync("/api/leads", new { name = "Customer", phone = "1234567890", sourceId = 1 });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var path = $"/api/leads/{id}";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(path,
            new { name = "Missing version", phone = "1234567890", sourceId = 1 })).StatusCode);
        var original = await admin.GetFromJsonAsync<JsonElement>(path);
        var version = original.GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(path,
            new { name = "First edit", phone = "1234567890", sourceId = 1, rowVersion = version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(path,
            new { name = "Stale edit", phone = "1234567890", sourceId = 1, rowVersion = version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync(path + "/status",
            new { statusId = 2, rowVersion = version })).StatusCode);
        var current = await admin.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("First edit", current.GetProperty("name").GetString());
        Assert.Equal(1, current.GetProperty("statusId").GetInt32());
    }
}
