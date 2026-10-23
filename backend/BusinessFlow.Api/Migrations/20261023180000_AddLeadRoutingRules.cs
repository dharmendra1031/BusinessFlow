using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

public partial class AddLeadRoutingRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LeadRoutingRules",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                SourceId = table.Column<long>(type: "bigint", nullable: false),
                TeamId = table.Column<long>(type: "bigint", nullable: false),
                MaxOpenLeads = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LeadRoutingRules", x => x.Id);
                table.CheckConstraint("CK_LeadRoutingRules_Capacity", "[MaxOpenLeads] BETWEEN 1 AND 500");
                table.ForeignKey("FK_LeadRoutingRules_LeadSources_SourceId",
                    x => x.SourceId, "LeadSources", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_LeadRoutingRules_Teams_TeamId",
                    x => x.TeamId, "Teams", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LeadRoutingRules_SourceId", table: "LeadRoutingRules",
            column: "SourceId", unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_LeadRoutingRules_TeamId", table: "LeadRoutingRules",
            column: "TeamId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "LeadRoutingRules");
}
