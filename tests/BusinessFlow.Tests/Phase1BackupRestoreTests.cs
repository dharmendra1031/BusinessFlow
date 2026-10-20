using BusinessFlow.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

public sealed class Phase1BackupRestoreTests
{
    [Fact]
    public async Task Backup_restores_to_isolated_catalog_and_upgrades_preserving_existing_records()
    {
        // Enable only in the disposable SQL Server CI service or an explicitly approved test instance.
        if (Environment.GetEnvironmentVariable("CRM_RESTORE_TEST") != "1") return;

        await using var database = await Phase1TestDatabase.CreateAsync("20261019133000_AddRolePermissionOverrides");
        var catalog = new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog;
        if (!catalog.StartsWith("LeadManagement_CITests_Phase1_", StringComparison.Ordinal))
            throw new InvalidOperationException("Restore test requires an isolated Phase 1 test catalog.");

        await using (var db = database.CreateContext())
        {
            // These columns existed before the Phase 1 organization and concurrency migrations.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO AspNetRoles (Name, NormalizedName) VALUES ('Manager', 'MANAGER');
                INSERT INTO AspNetUsers (FullName, IsActive, UserName, NormalizedUserName, EmailConfirmed,
                    PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
                VALUES ('Restored manager', 1, 'restore-manager', 'RESTORE-MANAGER', 0, 0, 0, 1, 0);
                INSERT INTO AspNetUserRoles (UserId, RoleId)
                    SELECT 1, Id FROM AspNetRoles WHERE Name = 'Manager';
                INSERT INTO Leads (Name, Phone, SourceId, StatusId, CreatedById, AssignedToId, CreatedAtUtc, UpdatedAtUtc)
                    VALUES ('Restored lead', '1234567890', 1, 5, 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                INSERT INTO DevelopmentProjects (LeadId, Title, Status, OwnerId, CreatedById, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (1, 'Restored project', 'Planning', 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                INSERT INTO DevelopmentTasks (ProjectId, Title, Status, Priority, AssignedToId, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (1, 'Restored task', 'Todo', 'Medium', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                INSERT INTO RolePermissionOverrides (RoleName, Permission, Enabled)
                    VALUES ('Manager', 'projects.view', 0);
                """);
        }

        var restored = "LeadManagement_CITests_Restore_" + Guid.NewGuid().ToString("N");
        var masterBuilder = new SqlConnectionStringBuilder(database.ConnectionString) { InitialCatalog = "master" };
        await using var master = new SqlConnection(masterBuilder.ConnectionString);
        await master.OpenAsync();

        var dataPath = (string?)await ScalarAsync(master,
            "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000))")
            ?? throw new InvalidOperationException("SQL Server did not provide its default data directory.");
        var separator = dataPath.Contains('\\') ? "\\" : "/";
        var directory = dataPath.TrimEnd('\\', '/') + separator;
        var backup = directory + restored + ".bak";

        try
        {
            await ExecuteAsync(master, $"BACKUP DATABASE {Identifier(catalog)} TO DISK = {Literal(backup)} " +
                "WITH COPY_ONLY, INIT, CHECKSUM;");
            await ExecuteAsync(master, $"RESTORE VERIFYONLY FROM DISK = {Literal(backup)} WITH CHECKSUM;");

            var moves = new List<string>();
            await using (var files = new SqlCommand(
                "SELECT name, type_desc FROM sys.master_files WHERE database_id = DB_ID(@catalog) ORDER BY file_id", master))
            {
                files.Parameters.AddWithValue("@catalog", catalog);
                await using var rows = await files.ExecuteReaderAsync();
                var index = 0;
                while (await rows.ReadAsync())
                {
                    var logicalName = rows.GetString(0);
                    var isLog = rows.GetString(1) == "LOG";
                    var target = directory + restored + "_" + index++ + (isLog ? ".ldf" : ".mdf");
                    moves.Add($"MOVE {Literal(logicalName)} TO {Literal(target)}");
                }
            }
            Assert.True(moves.Count >= 2, "SQL Server should report data and log files.");
            await ExecuteAsync(master, $"RESTORE DATABASE {Identifier(restored)} FROM DISK = {Literal(backup)} " +
                "WITH " + string.Join(", ", moves) + ", RECOVERY, CHECKSUM;");

            var restoredBuilder = new SqlConnectionStringBuilder(database.ConnectionString) { InitialCatalog = restored };
            await using var copy = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(restoredBuilder.ConnectionString).Options);
            var before = await copy.Database.GetAppliedMigrationsAsync();
            Assert.Contains("20261019133000_AddRolePermissionOverrides", before);
            Assert.DoesNotContain("20261020060037_AddDomainRowVersions", before);

            await copy.Database.MigrateAsync();
            Assert.Equal(1L, (await copy.Users.SingleAsync()).Id);
            var lead = await copy.Leads.SingleAsync();
            Assert.Equal(1L, lead.Id);
            Assert.Equal("Restored lead", lead.Name);
            Assert.Equal(5L, lead.StatusId);
            Assert.Equal(8, lead.RowVersion.Length);
            Assert.Equal(1L, (await copy.DevelopmentProjects.SingleAsync()).Id);
            Assert.Equal(8, (await copy.DevelopmentProjects.SingleAsync()).RowVersion.Length);
            Assert.Equal(1L, (await copy.DevelopmentTasks.SingleAsync()).Id);
            Assert.Equal(8, (await copy.DevelopmentTasks.SingleAsync()).RowVersion.Length);
            Assert.False((await copy.RolePermissionOverrides.SingleAsync()).Enabled);
            Assert.Contains("20261020061147_AddLeadStatusApprovals",
                await copy.Database.GetAppliedMigrationsAsync());
            await copy.Database.MigrateAsync(); // A second migration run must be idempotent.

            // The backup source remains on the original pre-upgrade migration.
            await using var original = database.CreateContext();
            Assert.DoesNotContain("20261020060037_AddDomainRowVersions",
                await original.Database.GetAppliedMigrationsAsync());
            // The old catalog lacks RowVersion, so use SQL instead of the current EF entity mapping.
            await using var originalConnection = new SqlConnection(database.ConnectionString);
            await originalConnection.OpenAsync();
            await using var sourceCheck = new SqlCommand("SELECT TOP (1) [Name] FROM [Leads]", originalConnection);
            Assert.Equal("Restored lead", (string?)await sourceCheck.ExecuteScalarAsync());
        }
        finally
        {
            // Only our own randomized restore catalog can be dropped; never drop the source catalog.
            if (!restored.StartsWith("LeadManagement_CITests_Restore_", StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid isolated restore catalog.");
            await ExecuteAsync(master, $"IF DB_ID({Literal(restored)}) IS NOT NULL BEGIN " +
                $"ALTER DATABASE {Identifier(restored)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE {Identifier(restored)}; END");
        }
    }

    private static string Identifier(string name) => "[" + name.Replace("]", "]]") + "]";
    private static string Literal(string value) => "N'" + value.Replace("'", "''") + "'";

    private static async Task<object?> ScalarAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 180 };
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 180 };
        await command.ExecuteNonQueryAsync();
    }
}
