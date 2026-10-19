using BusinessFlow.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace BusinessFlow.Api.Data;

public static class DbInitializer
{
    public static async Task SeedAdminAsync(IServiceProvider provider, IConfiguration config)
    {
        using var scope = provider.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<long>>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in new[] { "Admin", "Manager", "Employee" })
            if (!await roles.RoleExistsAsync(role))
            {
                var result = await roles.CreateAsync(new IdentityRole<long>(role));
                if (!result.Succeeded) throw new InvalidOperationException("Could not seed roles.");
            }

        var email = config["CRM_ADMIN_EMAIL"];
        var password = config["CRM_ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        if (await users.GetUsersInRoleAsync("Admin") is { Count: > 0 }) return;

        var user = new ApplicationUser
        {
            UserName = email, Email = email, FullName = "Administrator", EmailConfirmed = true, LockoutEnabled = true
        };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(x => x.Description)));
        var assigned = await users.AddToRoleAsync(user, "Admin");
        if (!assigned.Succeeded) throw new InvalidOperationException("Could not assign admin role.");
    }
}
