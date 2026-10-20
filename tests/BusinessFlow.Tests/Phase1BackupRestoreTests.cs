using BusinessFlow.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessFlow.Tests;

// A real SQL Server backup/restore rehearsal against a generated CI database only.
public sealed class Phase1BackupRestoreTests
{
    [Fact]
    public async Task Copy_only_backup_restores_and_upgrades_without_losing_records()
    {
        await using var source = await Phase1TestDatabase.CreateAsync("20261019133000_AddRolePermissionOverrides");
        await using (var db = source.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO AspNetRoles (Name, NormalizedName) VALUES ('Employee', 'EMPLOYEE');
                INSERT INTO AspNetUsers (FullName, IsActive, UserName, NormalizedUserName, EmailConfirmed,
                    PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
                VALUES ('Recovered employee', 1, 'recovered', 'RECOVERED', 0, 0, 0, 1, 0);
                INSERT INTO Leads (Name, Phone, SourceId, StatusId, CreatedById, AssignedToId, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('Recovered lead', '1234567890', 1, 5, 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                INSERT INTO DevelopmentProjects (LeadId, Title, Status, OwnerId, CreatedById, CreatedAtUtc, UpdatedAtUtc)
                VALUES (1, 'Recovered project', 'Planning', 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                INSERT INTO DevelopmentTasks (ProjectId, Title, Status, Priority, AssignedToId, CreatedAtUtc, UpdatedAtUtc)
                VALUES (1, 'Recovered task', 'Todo', 'Medium', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                INSERT INTO RolePermissionOverrides (RoleName, Permission, Enabled)
                VALUES ('Employee', 'projects.view', 0);
                """);
        }

        var sourceBuilder = new SqlConnectionStringBuilder(source.ConnectionString);
        var sourceName = sourceBuilder.InitialCatalog;
        // This fixture already rejects connections outside the dedicated CI database prefix.
        Assert.StartsWith("LeadManagement_CITests_Phase1_", sourceName, StringComparison.Ordinal);
        var restoredName = "LeadManagement_CITests_Phase1Restore_" + Guid.NewGuid().ToString("N");
        sourceBuilder.InitialCatalog = "master";
        await using var master = new SqlConnection(sourceBuilder.ConnectionString);
        await master.OpenAsync();

        var files = new List<(string LogicalName, string PhysicalName, int Type)>();
        using (var list = new SqlCommand("""
            SELECT name, physical_name, type
            FROM sys.master_files WHERE database_id = DB_ID(@database)
            ORDER BY file_id
            """, master))
        {
            list.Parameters.AddWithValue("@database", sourceName);
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                files.Add((reader.GetString(0), reader.GetString(1), reader.GetByte(2)));
        }
        Assert.Contains(files, file => file.Type == 0);
        Assert.Contains(files, file => file.Type == 1);

        // Server-side paths are derived from SQL Server's own existing data file locations.
        // They never reference the developer's machine or production filenames.
        var physical = files[0].PhysicalName;
        var separator = physical.LastIndexOfAny(['/', '\\']);
        Assert.True(separator >= 0, "SQL Server did not return an absolute physical data path.");
        var directory = physical[..(separator + 1)];
        var backup = directory + restoredName + ".bak";
        var moves = files.Select((file, index) =>
            "MOVE " + SqlString(file.LogicalName) + " TO " +
            SqlString(directory + restoredName + "_" + index + (file.Type == 1 ? ".ldf" : ".mdf")));
        var sourceSql = SqlName(sourceName);
        var targetSql = SqlName(restoredName);

        try
        {
            await ExecuteAsync(master, $"""
                IF DB_ID({SqlString(restoredName)}) IS NOT NULL
                    THROW 51001, 'Restore target already exists: refusing to overwrite it.', 1;
                BACKUP DATABASE {sourceSql} TO DISK = {SqlString(backup)}
                    WITH COPY_ONLY, INIT, CHECKSUM;
                RESTORE VERIFYONLY FROM DISK = {SqlString(backup)} WITH CHECKSUM;
                """);

            await ExecuteAsync(master, $"""
                RESTORE DATABASE {targetSql} FROM DISK = {SqlString(backup)}
                    WITH {string.Join(", ", moves)}, RECOVERY, CHECKSUM;
                """);

            var targetBuilder = new SqlConnectionStringBuilder(source.ConnectionString)
            {
                InitialCatalog = restoredName
            };
            await using (var restored = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(targetBuilder.ConnectionString).Options))
            {
                Assert.Equal("Recovered employee", await restored.Users.Select(x => x.FullName).SingleAsync());
                Assert.Equal("Recovered lead", await restored.Leads.Select(x => x.Name).SingleAsync());
                Assert.Equal("Recovered project", await restored.DevelopmentProjects.Select(x => x.Title).SingleAsync());
                Assert.Equal("Recovered task", await restored.DevelopmentTasks.Select(x => x.Title).SingleAsync());

                await restored.Database.MigrateAsync();
                Assert.Equal(8, (await restored.Leads.SingleAsync()).RowVersion.Length);
                Assert.Equal(8, (await restored.DevelopmentProjects.SingleAsync()).RowVersion.Length);
                Assert.Equal(8, (await restored.DevelopmentTasks.SingleAsync()).RowVersion.Length);
                Assert.Equal(1, await restored.RolePermissionOverrides.CountAsync(x =>
                    x.RoleName == "Employee" && x.Permission == "projects.view" && !x.Enabled));
                Assert.Equal("Recovered lead", await restored.Leads.Select(x => x.Name).SingleAsync());
            }

            await ExecuteAsync(master, $"DBCC CHECKDB ({targetSql}) WITH NO_INFOMSGS;");
        }
        finally
        {
            // Only the randomly named CI clone is removed; the source remains the fixture's responsibility.
            await ExecuteAsync(master, $"""
                IF DB_ID({SqlString(restoredName)}) IS NOT NULL
                BEGIN
                    ALTER DATABASE {targetSql} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE {targetSql};
                END
                """);
            // SQL Server retains the .bak until its isolated test container is discarded.
        }
    }

    private static string SqlName(string value) => "[" + value.Replace("]", "]]") + "]";
    private static string SqlString(string value) => "N'" + value.Replace("'", "''") + "'";

    private static async Task ExecuteAsync(SqlConnection connection, string commandText)
    {
        using var command = new SqlCommand(commandText, connection) { CommandTimeout = 180 };
        await command.ExecuteNonQueryAsync();
    }
}
