using System.Net.Http.Headers;
using BusinessFlow.Api.Data;
using BusinessFlow.Api.Models;
using BusinessFlow.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BusinessFlow.Tests;

internal sealed class Phase1ApiHost(Phase1TestDatabase database) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
            // Keep API security tests independent of CI/browser login-rate settings.
            ["RateLimiting:LoginAccountRequestsPerMinute"] = "5",
            ["RateLimiting:LoginIpRequestsPerMinute"] = "300",
            ["CRM_ADMIN_EMAIL"] = "", ["CRM_ADMIN_PASSWORD"] = ""
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(database.ConnectionString));
        });
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Error));
    }

    public async Task<HttpClient> ClientAsync(string role)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = role, UserName = Guid.NewGuid() + "@test.local" };
        user.Email = user.UserName;
        var created = await users.CreateAsync(user);
        if (!created.Succeeded) throw new InvalidOperationException(string.Join("; ", created.Errors.Select(x => x.Description)));
        await users.AddToRoleAsync(user, role);
        var token = scope.ServiceProvider.GetRequiredService<TokenService>().Create(user, [role]).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<HttpClient> ClientForUserAsync(long userId)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("Missing test user.");
        var token = scope.ServiceProvider.GetRequiredService<TokenService>().Create(user, await users.GetRolesAsync(user)).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
