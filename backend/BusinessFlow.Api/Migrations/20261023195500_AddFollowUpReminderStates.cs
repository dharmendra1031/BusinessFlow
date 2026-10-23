using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

public partial class AddFollowUpReminderStates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FollowUpReminderStates",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                FollowUpId = table.Column<long>(type: "bigint", nullable: false),
                UserId = table.Column<long>(type: "bigint", nullable: false),
                AcknowledgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                SnoozedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FollowUpReminderStates", x => x.Id);
                table.ForeignKey(
                    name: "FK_FollowUpReminderStates_LeadFollowUps_FollowUpId",
                    column: x => x.FollowUpId, principalTable: "LeadFollowUps",
                    principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_FollowUpReminderStates_AspNetUsers_UserId",
                    column: x => x.UserId, principalTable: "AspNetUsers",
                    principalColumn: "Id", onDelete: ReferentialAction.NoAction);
            });
        migrationBuilder.CreateIndex(
            name: "IX_FollowUpReminderStates_FollowUpId_UserId",
            table: "FollowUpReminderStates", columns: new[] { "FollowUpId", "UserId" }, unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_FollowUpReminderStates_UserId",
            table: "FollowUpReminderStates", column: "UserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("FollowUpReminderStates");
}
