using System;
using BusinessFlow.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261019070000_AddDevelopmentActivity")]
public sealed class AddDevelopmentActivity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DeveloperWorklogs",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProjectId = table.Column<long>(type: "bigint", nullable: false),
                TaskId = table.Column<long>(type: "bigint", nullable: true),
                UserId = table.Column<long>(type: "bigint", nullable: false),
                WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                Hours = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeveloperWorklogs", x => x.Id);
                table.ForeignKey("FK_DeveloperWorklogs_DevelopmentProjects_ProjectId", x => x.ProjectId,
                    "DevelopmentProjects", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_DeveloperWorklogs_DevelopmentTasks_TaskId", x => x.TaskId,
                    "DevelopmentTasks", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_DeveloperWorklogs_AspNetUsers_UserId", x => x.UserId,
                    "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateTable(
            name: "ProjectBugs",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProjectId = table.Column<long>(type: "bigint", nullable: false),
                TaskId = table.Column<long>(type: "bigint", nullable: true),
                Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                AssignedToId = table.Column<long>(type: "bigint", nullable: true),
                ReportedById = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectBugs", x => x.Id);
                table.ForeignKey("FK_ProjectBugs_DevelopmentProjects_ProjectId", x => x.ProjectId,
                    "DevelopmentProjects", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_ProjectBugs_DevelopmentTasks_TaskId", x => x.TaskId,
                    "DevelopmentTasks", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_ProjectBugs_AspNetUsers_AssignedToId", x => x.AssignedToId,
                    "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_ProjectBugs_AspNetUsers_ReportedById", x => x.ReportedById,
                    "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex("IX_DeveloperWorklogs_ProjectId_WorkDate", "DeveloperWorklogs",
            new[] { "ProjectId", "WorkDate" });
        migrationBuilder.CreateIndex("IX_DeveloperWorklogs_UserId_WorkDate", "DeveloperWorklogs",
            new[] { "UserId", "WorkDate" });
        migrationBuilder.CreateIndex("IX_DeveloperWorklogs_TaskId", "DeveloperWorklogs", "TaskId");
        migrationBuilder.CreateIndex("IX_ProjectBugs_ProjectId_Status", "ProjectBugs",
            new[] { "ProjectId", "Status" });
        migrationBuilder.CreateIndex("IX_ProjectBugs_AssignedToId", "ProjectBugs", "AssignedToId");
        migrationBuilder.CreateIndex("IX_ProjectBugs_ReportedById", "ProjectBugs", "ReportedById");
        migrationBuilder.CreateIndex("IX_ProjectBugs_TaskId", "ProjectBugs", "TaskId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("DeveloperWorklogs");
        migrationBuilder.DropTable("ProjectBugs");
    }
}
