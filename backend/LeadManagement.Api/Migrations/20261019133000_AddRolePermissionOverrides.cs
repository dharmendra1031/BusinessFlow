using LeadManagement.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadManagement.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261019133000_AddRolePermissionOverrides")]
public sealed class AddRolePermissionOverrides : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RolePermissionOverrides",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                RoleName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Permission = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                Enabled = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_RolePermissionOverrides", x => x.Id));
        migrationBuilder.CreateIndex(
            name: "IX_RolePermissionOverrides_RoleName_Permission",
            table: "RolePermissionOverrides",
            columns: new[] { "RoleName", "Permission" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("RolePermissionOverrides");
}
