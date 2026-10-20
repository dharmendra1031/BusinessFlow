using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadStatusApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeadStatusApprovals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedStatusId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedById = table.Column<long>(type: "bigint", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeadVersion = table.Column<byte[]>(type: "varbinary(8)", maxLength: 8, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecidedById = table.Column<long>(type: "bigint", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadStatusApprovals", x => x.Id);
                    table.CheckConstraint("CK_LeadStatusApprovals_State", "[State] IN (N'Pending', N'Approved', N'Rejected')");
                    table.ForeignKey(
                        name: "FK_LeadStatusApprovals_AspNetUsers_DecidedById",
                        column: x => x.DecidedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeadStatusApprovals_AspNetUsers_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeadStatusApprovals_LeadStatuses_RequestedStatusId",
                        column: x => x.RequestedStatusId,
                        principalTable: "LeadStatuses",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeadStatusApprovals_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeadStatusApprovals_DecidedById",
                table: "LeadStatusApprovals",
                column: "DecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_LeadStatusApprovals_LeadId",
                table: "LeadStatusApprovals",
                column: "LeadId",
                unique: true,
                filter: "[State] = N'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_LeadStatusApprovals_RequestedById",
                table: "LeadStatusApprovals",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_LeadStatusApprovals_RequestedStatusId",
                table: "LeadStatusApprovals",
                column: "RequestedStatusId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadStatusApprovals");
        }
    }
}
