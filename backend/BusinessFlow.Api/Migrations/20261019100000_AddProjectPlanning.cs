using System;
using BusinessFlow.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261019100000_AddProjectPlanning")]
public sealed class AddProjectPlanning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProjectMilestones",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProjectId = table.Column<long>(type: "bigint", nullable: false),
                Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectMilestones", x => x.Id);
                table.ForeignKey("FK_ProjectMilestones_DevelopmentProjects_ProjectId", x => x.ProjectId,
                    "DevelopmentProjects", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ProjectSprints",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProjectId = table.Column<long>(type: "bigint", nullable: false),
                Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Goal = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectSprints", x => x.Id);
                table.ForeignKey("FK_ProjectSprints_DevelopmentProjects_ProjectId", x => x.ProjectId,
                    "DevelopmentProjects", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddColumn<long>("MilestoneId", "DevelopmentTasks", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>("SprintId", "DevelopmentTasks", type: "bigint", nullable: true);
        migrationBuilder.CreateIndex("IX_ProjectMilestones_ProjectId_DueDate", "ProjectMilestones",
            new[] { "ProjectId", "DueDate" });
        migrationBuilder.CreateIndex("IX_ProjectSprints_ProjectId_StartDate", "ProjectSprints",
            new[] { "ProjectId", "StartDate" });
        migrationBuilder.CreateIndex("IX_DevelopmentTasks_MilestoneId", "DevelopmentTasks", "MilestoneId");
        migrationBuilder.CreateIndex("IX_DevelopmentTasks_SprintId", "DevelopmentTasks", "SprintId");
        migrationBuilder.AddForeignKey(
            name: "FK_DevelopmentTasks_ProjectMilestones_MilestoneId",
            table: "DevelopmentTasks",
            column: "MilestoneId",
            principalTable: "ProjectMilestones",
            principalColumn: "Id",
            onDelete: ReferentialAction.NoAction);
        migrationBuilder.AddForeignKey(
            name: "FK_DevelopmentTasks_ProjectSprints_SprintId",
            table: "DevelopmentTasks",
            column: "SprintId",
            principalTable: "ProjectSprints",
            principalColumn: "Id",
            onDelete: ReferentialAction.NoAction);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_DevelopmentTasks_ProjectMilestones_MilestoneId", "DevelopmentTasks");
        migrationBuilder.DropForeignKey("FK_DevelopmentTasks_ProjectSprints_SprintId", "DevelopmentTasks");
        migrationBuilder.DropIndex("IX_DevelopmentTasks_MilestoneId", "DevelopmentTasks");
        migrationBuilder.DropIndex("IX_DevelopmentTasks_SprintId", "DevelopmentTasks");
        migrationBuilder.DropColumn("MilestoneId", "DevelopmentTasks");
        migrationBuilder.DropColumn("SprintId", "DevelopmentTasks");
        migrationBuilder.DropTable("ProjectMilestones");
        migrationBuilder.DropTable("ProjectSprints");
    }
}
