using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

public partial class AddLeadSourceManagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            table: "LeadSources",
            type: "bit",
            nullable: false,
            defaultValue: true);

        // Keep historical lead-source rows intact. Resolve legacy duplicate names before
        // applying the unique index rather than silently merging sources referenced by leads.
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM dbo.LeadSources
                GROUP BY [Name] COLLATE Latin1_General_100_CI_AS
                HAVING COUNT_BIG(*) > 1
            )
                THROW 51002, 'Duplicate lead source names exist; resolve before migration.', 1;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_LeadSources_Name",
            table: "LeadSources",
            column: "Name",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_LeadSources_Name", table: "LeadSources");
        migrationBuilder.DropColumn(name: "IsActive", table: "LeadSources");
    }
}
