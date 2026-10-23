using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1ProductionCspTests
{
    [Fact]
    public async Task Production_csp_allows_pdf_blob_frames_without_relaxing_other_guards()
    {
        await using var database = await Phase1TestDatabase.CreateAsync();
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                    ["Jwt:Key"] = "BusinessFlow_Production_Csp_Test_Key_2026",
                    ["Jwt:Issuer"] = "BusinessFlow.Tests",
                    ["Jwt:Audience"] = "BusinessFlow.Tests",
                    ["CRM_ADMIN_EMAIL"] = "",
                    ["CRM_ADMIN_PASSWORD"] = ""
                }));
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var values));
        var csp = Assert.Single(values);
        Assert.Contains("frame-src 'self' blob:;", csp);
        Assert.Contains("object-src 'none';", csp);
        Assert.Contains("frame-ancestors 'none';", csp);
        Assert.Contains("script-src 'self';", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
    }
}
