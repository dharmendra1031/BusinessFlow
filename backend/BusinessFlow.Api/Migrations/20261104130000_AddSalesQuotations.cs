using BusinessFlow.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104130000_AddSalesQuotations")]
public partial class AddSalesQuotations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SalesQuotations",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                LeadId = table.Column<long>(type: "bigint", nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                CurrentRevision = table.Column<int>(type: "int", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalesQuotations", x => x.Id);
                table.CheckConstraint("CK_SalesQuotations_Status",
                    "[Status] IN (N'Draft', N'Sent', N'Accepted', N'Rejected')");
                table.ForeignKey("FK_SalesQuotations_Leads_LeadId", x => x.LeadId,
                    "Leads", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_SalesQuotations_AspNetUsers_CreatedById", x => x.CreatedById,
                    "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateTable(
            name: "SalesQuotationRevisions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                QuotationId = table.Column<long>(type: "bigint", nullable: false),
                Revision = table.Column<int>(type: "int", nullable: false),
                Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Scope = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                PaymentTerms = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalesQuotationRevisions", x => x.Id);
                table.CheckConstraint("CK_SalesQuotationRevisions_Amount", "[Amount] > 0");
                table.ForeignKey("FK_SalesQuotationRevisions_SalesQuotations_QuotationId",
                    x => x.QuotationId, "SalesQuotations", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_SalesQuotationRevisions_AspNetUsers_CreatedById",
                    x => x.CreatedById, "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex(name: "IX_SalesQuotations_LeadId",
            table: "SalesQuotations", column: "LeadId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_SalesQuotations_CreatedById",
            table: "SalesQuotations", column: "CreatedById");
        migrationBuilder.CreateIndex(name: "IX_SalesQuotationRevisions_QuotationId_Revision",
            table: "SalesQuotationRevisions", columns: new[] { "QuotationId", "Revision" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_SalesQuotationRevisions_CreatedById",
            table: "SalesQuotationRevisions", column: "CreatedById");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("SalesQuotationRevisions");
        migrationBuilder.DropTable("SalesQuotations");
    }
}
