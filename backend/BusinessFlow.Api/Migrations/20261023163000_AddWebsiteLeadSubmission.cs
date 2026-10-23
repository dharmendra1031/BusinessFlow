using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

public partial class AddWebsiteLeadSubmission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WebsiteLeadSubmissions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                LeadId = table.Column<long>(type: "bigint", nullable: false),
                SubmissionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WebsiteLeadSubmissions", x => x.Id);
                table.ForeignKey("FK_WebsiteLeadSubmissions_Leads_LeadId",
                    x => x.LeadId, "Leads", "Id", onDelete: ReferentialAction.NoAction);
            });
        migrationBuilder.CreateIndex(
            name: "IX_WebsiteLeadSubmissions_SubmissionHash",
            table: "WebsiteLeadSubmissions", column: "SubmissionHash", unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_WebsiteLeadSubmissions_LeadId",
            table: "WebsiteLeadSubmissions", column: "LeadId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "WebsiteLeadSubmissions");
}
