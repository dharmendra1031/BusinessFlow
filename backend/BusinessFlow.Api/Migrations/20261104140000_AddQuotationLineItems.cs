using BusinessFlow.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104140000_AddQuotationLineItems")]
public partial class AddQuotationLineItems : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>("Subtotal", "SalesQuotationRevisions", type: "decimal(18,3)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("DiscountAmount", "SalesQuotationRevisions", type: "decimal(18,3)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("TaxAmount", "SalesQuotationRevisions", type: "decimal(18,3)", nullable: false, defaultValue: 0m);
        migrationBuilder.Sql("UPDATE [SalesQuotationRevisions] SET [Subtotal] = [Amount]");

        migrationBuilder.CreateTable(
            name: "SalesQuotationLineItems",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                RevisionId = table.Column<long>(type: "bigint", nullable: false),
                Position = table.Column<int>(type: "int", nullable: false),
                Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                UnitPrice = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                DiscountRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                TaxRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                BaseAmount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                DiscountAmount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                TaxAmount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                TotalAmount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalesQuotationLineItems", x => x.Id);
                table.CheckConstraint("CK_SalesQuotationLineItems_Values", "[Quantity] > 0 AND [UnitPrice] >= 0 AND [DiscountRate] BETWEEN 0 AND 100 AND [TaxRate] BETWEEN 0 AND 100");
                table.ForeignKey("FK_SalesQuotationLineItems_SalesQuotationRevisions_RevisionId",
                    x => x.RevisionId, "SalesQuotationRevisions", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex("IX_SalesQuotationLineItems_RevisionId_Position",
            "SalesQuotationLineItems", new[] { "RevisionId", "Position" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("SalesQuotationLineItems");
        migrationBuilder.DropColumn("Subtotal", "SalesQuotationRevisions");
        migrationBuilder.DropColumn("DiscountAmount", "SalesQuotationRevisions");
        migrationBuilder.DropColumn("TaxAmount", "SalesQuotationRevisions");
    }
}
