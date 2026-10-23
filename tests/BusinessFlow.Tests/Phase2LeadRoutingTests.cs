using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessFlow.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase2LeadRoutingTests
{
    private const string SigningKey = "BusinessFlow_Routing_Test_Signing_Secret_123456";

    private static HttpRequestMessage WebsiteRequest(string eventId)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            eventId, name = "Routed visitor", phone = "+91 9876543210", consentAccepted = true
        });
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var prefix = Encoding.UTF8.GetBytes(stamp + ".");
        var signatureBytes = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signatureBytes, 0);
        body.CopyTo(signatureBytes, prefix.Length);
        var digest = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(SigningKey), signatureBytes));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/website/leads")
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Website-Timestamp", stamp);
        request.Headers.Add("X-Website-Signature", "sha256=" + digest);
        return request;
    }

    [Fact]
    public async Task Website_routes_to_least_loaded_team_member_and_preserves_assignment_history()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var seedHost = new Phase1ApiHost(database);
        using var admin = await seedHost.ClientAsync("Admin");
        using var employee = await seedHost.ClientAsync("Employee");
        var ownerId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt64();

        long firstId;
        long secondId;
        long teamId;
        await using (var db = database.CreateContext())
        {
            var department = new Department { Name = "Inbound sales" };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
            var team = new Team { Name = "Website support", DepartmentId = department.Id };
            db.Teams.Add(team);
            var first = new ApplicationUser { FullName = "First sales agent",
                UserName = "route-first@test.local", DepartmentId = department.Id, IsActive = true };
            var second = new ApplicationUser { FullName = "Second sales agent",
                UserName = "route-second@test.local", DepartmentId = department.Id, IsActive = true };
            db.Users.AddRange(first, second);
            await db.SaveChangesAsync();
            db.TeamMemberships.AddRange(
                new TeamMembership { TeamId = team.Id, UserId = first.Id },
                new TeamMembership { TeamId = team.Id, UserId = second.Id });
            db.Leads.Add(new Lead { Name = "Existing open lead", Phone = "9999999999",
                SourceId = 1, StatusId = 1, CreatedById = ownerId, AssignedToId = first.Id });
            await db.SaveChangesAsync();
            firstId = first.Id;
            secondId = second.Id;
            teamId = team.Id;
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/admin/lead-routing")).StatusCode);
        var rule = new { sourceId = 2, teamId, maxOpenLeads = 2, isActive = true };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await employee.PostAsJsonAsync("/api/admin/lead-routing", rule)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync("/api/admin/lead-routing", rule)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/admin/lead-routing", rule)).StatusCode);

        await using var host = new Phase1ApiHost(database, new Dictionary<string, string?>
        {
            ["WebsiteIntake:Enabled"] = "true",
            ["WebsiteIntake:SigningSecret"] = SigningKey,
            ["WebsiteIntake:OwnerUserId"] = ownerId.ToString()
        });
        using var web = host.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost") });

        Assert.Equal(HttpStatusCode.Accepted,
            (await web.SendAsync(WebsiteRequest("lead-route-event-1001"))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await web.SendAsync(WebsiteRequest("lead-route-event-1002"))).StatusCode);

        await using (var db = database.CreateContext())
        {
            var routed = await db.Leads.Where(x => x.SourceId == 2).OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(2, routed.Count);
            Assert.Equal(secondId, routed[0].AssignedToId);
            Assert.Equal(firstId, routed[1].AssignedToId);
            Assert.All(routed, x => Assert.Equal(ownerId, x.CreatedById));
            var histories = await db.LeadAssignments.OrderBy(x => x.LeadId).ToListAsync();
            Assert.Equal(2, histories.Count);
            Assert.Equal(secondId, histories[0].AssignedToId);
            Assert.Equal(firstId, histories[1].AssignedToId);
            Assert.True(await db.AuditLogs.CountAsync(x => x.Action == "Assign" &&
                x.ChangedFields!.StartsWith("Automatic source routing")) == 2);
        }
        // First member is at 2/2, second is at 1/2, so one slot remains.
        Assert.Equal(HttpStatusCode.Accepted,
            (await web.SendAsync(WebsiteRequest("lead-route-event-1003"))).StatusCode);
        await using (var db = database.CreateContext())
        {
            Assert.Equal(secondId, await db.Leads.Where(x => x.SourceId == 2)
                .OrderByDescending(x => x.Id).Select(x => x.AssignedToId).FirstAsync());
        }
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await web.SendAsync(WebsiteRequest("lead-route-event-1004"))).StatusCode);
        await using (var db = database.CreateContext())
        {
            Assert.Equal(4, await db.Leads.CountAsync()); // Existing + three routed.
            Assert.Equal(3, await db.WebsiteLeadSubmissions.CountAsync());
        }

        long ruleId;
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/admin/lead-routing");
        ruleId = all[0].GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(
            "/api/admin/lead-routing/" + ruleId,
            new { sourceId = 2, teamId, maxOpenLeads = 2, isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await web.SendAsync(WebsiteRequest("lead-route-event-1004"))).StatusCode);
        await using (var db = database.CreateContext())
        {
            Assert.Equal(ownerId, await db.Leads.Where(x => x.SourceId == 2)
                .OrderByDescending(x => x.Id).Select(x => x.AssignedToId).FirstAsync());
            Assert.Equal(3, await db.LeadAssignments.CountAsync());
        }
    }

    [Fact]
    public async Task Invalid_capacity_and_inactive_team_are_denied_without_creating_rules()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var admin = await host.ClientAsync("Admin");

        long teamId;
        await using (var db = database.CreateContext())
        {
            var department = new Department { Name = "Inactive division" };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
            var team = new Team { Name = "Inactive team", DepartmentId = department.Id, IsActive = false };
            db.Teams.Add(team);
            await db.SaveChangesAsync();
            teamId = team.Id;
        }
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/admin/lead-routing",
                new { sourceId = 2, teamId, maxOpenLeads = 0, isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/admin/lead-routing",
                new { sourceId = 2, teamId, maxOpenLeads = 10, isActive = true })).StatusCode);
        await using var verify = database.CreateContext();
        Assert.Equal(0, await verify.LeadRoutingRules.CountAsync());
    }
}
