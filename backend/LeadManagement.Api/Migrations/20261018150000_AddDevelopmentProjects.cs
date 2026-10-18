using System;
using LeadManagement.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadManagement.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261018150000_AddDevelopmentProjects")]
public sealed class AddDevelopmentProjects : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DevelopmentProjects",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                LeadId = table.Column<long>(type: "bigint", nullable: false),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                OwnerId = table.Column<long>(type: "bigint", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DevelopmentProjects", x => x.Id);
                table.ForeignKey("FK_DevelopmentProjects_Leads_LeadId", x => x.LeadId,
                    "Leads", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_DevelopmentProjects_AspNetUsers_OwnerId", x => x.OwnerId,
                    "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_DevelopmentProjects_AspNetUsers_CreatedById", x => x.CreatedById,
                    "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateTable(
            name: "DevelopmentTasks",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProjectId = table.Column<long>(type: "bigint", nullable: false),
                Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                AssignedToId = table.Column<long>(type: "bigint", nullable: true),
                DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DevelopmentTasks", x => x.Id);
                table.ForeignKey("FK_DevelopmentTasks_DevelopmentProjects_ProjectId", x => x.ProjectId,
                    "DevelopmentProjects", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_DevelopmentTasks_AspNetUsers_AssignedToId", x => x.AssignedToId,
                    "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_DevelopmentTasks_AspNetUsers_CreatedById", x => x.CreatedById,
                    "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex("IX_DevelopmentProjects_LeadId", "DevelopmentProjects", "LeadId");
        migrationBuilder.CreateIndex("IX_DevelopmentProjects_CreatedById", "DevelopmentProjects", "CreatedById");
        migrationBuilder.CreateIndex("IX_DevelopmentProjects_OwnerId_Status", "DevelopmentProjects",
            new[] { "OwnerId", "Status" });
        migrationBuilder.CreateIndex("IX_DevelopmentTasks_ProjectId_Status", "DevelopmentTasks",
            new[] { "ProjectId", "Status" });
        migrationBuilder.CreateIndex("IX_DevelopmentTasks_AssignedToId", "DevelopmentTasks", "AssignedToId");
        migrationBuilder.CreateIndex("IX_DevelopmentTasks_CreatedById", "DevelopmentTasks", "CreatedById");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("DevelopmentTasks");
        migrationBuilder.DropTable("DevelopmentProjects");
    }
}
