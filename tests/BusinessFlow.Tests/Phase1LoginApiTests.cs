using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1LoginApiTests
{
    [Fact]
    public async Task Missing_account_attempts_are_limited_across_spoofed_forwarded_addresses()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new Phase1ApiHost(database);
        using var client = host.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2." + attempt);
            var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "missing@example.test", password = "WrongPassword123!" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        var limited = await client.PostAsJsonAsync("/api/auth/login", new { email = "MISSING@example.test", password = "WrongPassword123!" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var other = await client.PostAsJsonAsync("/api/auth/login", new { email = "different@example.test", password = "WrongPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }
}
