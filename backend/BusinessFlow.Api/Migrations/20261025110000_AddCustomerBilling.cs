using BusinessFlow.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261025110000_AddCustomerBilling")]
public partial class AddCustomerBilling : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LegalName", table: "Customers", type: "nvarchar(200)",
            maxLength: 200, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Gstin", table: "Customers", type: "nvarchar(15)",
            maxLength: 15, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BillingAddress", table: "Customers", type: "nvarchar(500)",
            maxLength: 500, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BillingCity", table: "Customers", type: "nvarchar(100)",
            maxLength: 100, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BillingState", table: "Customers", type: "nvarchar(100)",
            maxLength: 100, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BillingPostalCode", table: "Customers", type: "nvarchar(20)",
            maxLength: 20, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BillingCountry", table: "Customers", type: "nvarchar(100)",
            maxLength: 100, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NormalizedPhone", table: "Customers", type: "nvarchar(30)",
            maxLength: 30, nullable: true, computedColumnSql: "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE([Phone], N' ', N''), N'-', N''), N'+', N''), N'(', N''), N')', N''), N'.', N''), NCHAR(9), N'')", stored: true);

        migrationBuilder.CreateIndex(name: "IX_Customers_Email", table: "Customers", column: "Email");
        migrationBuilder.CreateIndex(name: "IX_Customers_Gstin", table: "Customers", column: "Gstin");
        migrationBuilder.CreateIndex(name: "IX_Customers_NormalizedPhone", table: "Customers", column: "NormalizedPhone");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Customers_Email", table: "Customers");
        migrationBuilder.DropIndex(name: "IX_Customers_Gstin", table: "Customers");
        migrationBuilder.DropIndex(name: "IX_Customers_NormalizedPhone", table: "Customers");
        migrationBuilder.DropColumn(name: "LegalName", table: "Customers");
        migrationBuilder.DropColumn(name: "Gstin", table: "Customers");
        migrationBuilder.DropColumn(name: "BillingAddress", table: "Customers");
        migrationBuilder.DropColumn(name: "BillingCity", table: "Customers");
        migrationBuilder.DropColumn(name: "BillingState", table: "Customers");
        migrationBuilder.DropColumn(name: "BillingPostalCode", table: "Customers");
        migrationBuilder.DropColumn(name: "BillingCountry", table: "Customers");
        migrationBuilder.DropColumn(name: "NormalizedPhone", table: "Customers");
    }
}
