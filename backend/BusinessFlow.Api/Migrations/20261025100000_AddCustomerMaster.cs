using BusinessFlow.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261025100000_AddCustomerMaster")]
public partial class AddCustomerMaster : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Customers",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Customers", x => x.Id));

        migrationBuilder.CreateTable(
            name: "CustomerContacts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                CustomerId = table.Column<long>(type: "bigint", nullable: false),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Designation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerContacts", x => x.Id);
                table.ForeignKey("FK_CustomerContacts_Customers_CustomerId", x => x.CustomerId,
                    "Customers", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.AddColumn<long>(
            name: "CustomerId", table: "Leads", type: "bigint", nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Customers_Name", table: "Customers", column: "Name");
        migrationBuilder.CreateIndex(
            name: "IX_CustomerContacts_CustomerId_IsPrimary", table: "CustomerContacts",
            columns: new[] { "CustomerId", "IsPrimary" });
        migrationBuilder.CreateIndex(
            name: "IX_Leads_CustomerId", table: "Leads", column: "CustomerId");

        migrationBuilder.AddForeignKey(
            name: "FK_Leads_Customers_CustomerId", table: "Leads", column: "CustomerId",
            principalTable: "Customers", principalColumn: "Id", onDelete: ReferentialAction.NoAction);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_Leads_Customers_CustomerId", "Leads");
        migrationBuilder.DropTable("CustomerContacts");
        migrationBuilder.DropTable("Customers");
        migrationBuilder.DropIndex("IX_Leads_CustomerId", "Leads");
        migrationBuilder.DropColumn("CustomerId", "Leads");
    }
}
