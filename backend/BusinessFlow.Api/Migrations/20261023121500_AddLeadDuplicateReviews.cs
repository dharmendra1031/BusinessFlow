using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

public partial class AddLeadDuplicateReviews : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NormalizedPhone", table: "Leads", type: "nvarchar(30)",
            maxLength: 30, nullable: false,
            computedColumnSql: "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE([Phone], N' ', N''), N'-', N''), N'+', N''), N'(', N''), N')', N''), N'.', N''), NCHAR(9), N'')", stored: true);

        migrationBuilder.AddColumn<string>(
            name: "NormalizedEmail", table: "Leads", type: "nvarchar(256)",
            maxLength: 256, nullable: true,
            computedColumnSql: "NULLIF(LOWER(LTRIM(RTRIM([Email]))), N'')", stored: true);

        migrationBuilder.CreateIndex(
            name: "IX_Leads_NormalizedPhone", table: "Leads", column: "NormalizedPhone");
        migrationBuilder.CreateIndex(
            name: "IX_Leads_NormalizedEmail", table: "Leads", column: "NormalizedEmail");

        migrationBuilder.CreateTable(
            name: "LeadDuplicateReviews",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                LeftLeadId = table.Column<long>(type: "bigint", nullable: false),
                RightLeadId = table.Column<long>(type: "bigint", nullable: false),
                Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                ReviewedById = table.Column<long>(type: "bigint", nullable: false),
                ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LeadDuplicateReviews", x => x.Id);
                table.CheckConstraint("CK_LeadDuplicateReviews_Order", "[LeftLeadId] < [RightLeadId]");
                table.CheckConstraint("CK_LeadDuplicateReviews_Decision",
                    "[Decision] IN (N'Confirmed', N'NotDuplicate')");
                table.ForeignKey("FK_LeadDuplicateReviews_Leads_LeftLeadId",
                    x => x.LeftLeadId, "Leads", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_LeadDuplicateReviews_Leads_RightLeadId",
                    x => x.RightLeadId, "Leads", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_LeadDuplicateReviews_AspNetUsers_ReviewedById",
                    x => x.ReviewedById, "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LeadDuplicateReviews_LeftLeadId_RightLeadId",
            table: "LeadDuplicateReviews", columns: new[] { "LeftLeadId", "RightLeadId" }, unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_LeadDuplicateReviews_RightLeadId",
            table: "LeadDuplicateReviews", column: "RightLeadId");
        migrationBuilder.CreateIndex(
            name: "IX_LeadDuplicateReviews_ReviewedById",
            table: "LeadDuplicateReviews", column: "ReviewedById");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("LeadDuplicateReviews");
        migrationBuilder.DropIndex("IX_Leads_NormalizedPhone", "Leads");
        migrationBuilder.DropIndex("IX_Leads_NormalizedEmail", "Leads");
        migrationBuilder.DropColumn("NormalizedPhone", "Leads");
        migrationBuilder.DropColumn("NormalizedEmail", "Leads");
    }
}
