using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RolePermissionGrants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissionGrants", x => x.Id);
                    table.CheckConstraint("CK_RolePermissionGrants_Scope", "[Scope] BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_RolePermissionGrants_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserPermissionRestrictions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissionRestrictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPermissionRestrictions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissionGrants_RoleId_Permission",
                table: "RolePermissionGrants",
                columns: new[] { "RoleId", "Permission" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissionRestrictions_UserId_Permission",
                table: "UserPermissionRestrictions",
                columns: new[] { "UserId", "Permission" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RolePermissionGrants");

            migrationBuilder.DropTable(
                name: "UserPermissionRestrictions");
        }
    }
}
