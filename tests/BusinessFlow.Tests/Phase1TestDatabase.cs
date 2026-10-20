using BusinessFlow.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Tests;

internal sealed class Phase1TestDatabase : IAsyncDisposable
{
    private readonly string connection;
    public string ConnectionString => connection;
    private Phase1TestDatabase(string connection) => this.connection = connection;

    public static async Task<Phase1TestDatabase> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable("CRM_TEST_CONNECTION")
            ?? throw new InvalidOperationException("CRM_TEST_CONNECTION is required.");
        var builder = new SqlConnectionStringBuilder(configured);
        if (!builder.InitialCatalog.StartsWith("LeadManagement_CITests", StringComparison.Ordinal))
            throw new InvalidOperationException("Phase 1 tests require a dedicated test database.");
        builder.InitialCatalog = "LeadManagement_CITests_Phase1_" + Guid.NewGuid().ToString("N");
        var fixture = new Phase1TestDatabase(builder.ConnectionString);
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        return fixture;
    }

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(connection).Options);

    public async ValueTask DisposeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }
}
