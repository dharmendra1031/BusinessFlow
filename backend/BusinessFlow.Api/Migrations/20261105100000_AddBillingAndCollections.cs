using BusinessFlow.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261105100000_AddBillingAndCollections")]
public partial class AddBillingAndCollections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "BilledAmount", table: "SalesContracts", type: "decimal(18,3)",
            precision: 18, scale: 3, nullable: false, defaultValue: 0m);

        migrationBuilder.CreateTable(
            name: "SalesInvoices",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ContractId = table.Column<long>(type: "bigint", nullable: false),
                Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                BillToName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                PaidAmount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                VoidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalesInvoices", x => x.Id);
                table.CheckConstraint("CK_SalesInvoices_Type",
                    "[Type] IN (N'Advance', N'Milestone', N'Final')");
                table.CheckConstraint("CK_SalesInvoices_Status",
                    "[Status] IN (N'Issued', N'PartiallyPaid', N'Paid', N'Void')");
                table.CheckConstraint("CK_SalesInvoices_Amounts",
                    "[Amount] > 0 AND [PaidAmount] >= 0 AND [PaidAmount] <= [Amount]");
                table.ForeignKey("FK_SalesInvoices_SalesContracts_ContractId", x => x.ContractId,
                    "SalesContracts", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_SalesInvoices_AspNetUsers_CreatedById", x => x.CreatedById,
                    "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateTable(
            name: "SalesPayments",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalesPayments", x => x.Id);
                table.CheckConstraint("CK_SalesPayments_Amount", "[Amount] > 0");
                table.CheckConstraint("CK_SalesPayments_Method",
                    "[Method] IN (N'BankTransfer', N'UPI', N'Cash', N'Card', N'Other')");
                table.ForeignKey("FK_SalesPayments_SalesInvoices_InvoiceId", x => x.InvoiceId,
                    "SalesInvoices", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_SalesPayments_AspNetUsers_CreatedById", x => x.CreatedById,
                    "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex("IX_SalesInvoices_ContractId_Status",
            "SalesInvoices", new[] { "ContractId", "Status" });
        migrationBuilder.CreateIndex("IX_SalesInvoices_CreatedById",
            "SalesInvoices", "CreatedById");
        migrationBuilder.CreateIndex("IX_SalesPayments_InvoiceId_ReceivedOn",
            "SalesPayments", new[] { "InvoiceId", "ReceivedOn" });
        migrationBuilder.CreateIndex("IX_SalesPayments_CreatedById",
            "SalesPayments", "CreatedById");
        migrationBuilder.CreateIndex("IX_SalesPayments_Reference",
            "SalesPayments", "Reference", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("SalesPayments");
        migrationBuilder.DropTable("SalesInvoices");
        migrationBuilder.DropColumn("BilledAmount", "SalesContracts");
    }
}
