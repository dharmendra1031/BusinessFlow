using BusinessFlow.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261105110000_AddSalesCreditsAndRefunds")]
public partial class AddSalesCreditsAndRefunds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_SalesInvoices_Status", "SalesInvoices");
        migrationBuilder.DropCheckConstraint("CK_SalesInvoices_Amounts", "SalesInvoices");
        migrationBuilder.DropCheckConstraint("CK_SalesPayments_Amount", "SalesPayments");
        migrationBuilder.AddColumn<decimal>("CreditedAmount", "SalesInvoices",
            type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("RefundedAmount", "SalesInvoices",
            type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("RefundedAmount", "SalesPayments",
            type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValue: 0m);
        migrationBuilder.AddCheckConstraint("CK_SalesInvoices_Status", "SalesInvoices",
            "[Status] IN (N'Issued', N'PartiallyPaid', N'Paid', N'Void', N'RefundDue', N'Credited')");
        migrationBuilder.AddCheckConstraint("CK_SalesInvoices_Amounts", "SalesInvoices",
            "[Amount] > 0 AND [PaidAmount] >= 0 AND [PaidAmount] <= [Amount] AND [CreditedAmount] >= 0 AND [CreditedAmount] <= [Amount] AND [RefundedAmount] >= 0");
        migrationBuilder.AddCheckConstraint("CK_SalesPayments_Amount", "SalesPayments",
            "[Amount] > 0 AND [RefundedAmount] >= 0 AND [RefundedAmount] <= [Amount]");

        migrationBuilder.CreateTable("SalesCreditNotes",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalesCreditNotes", x => x.Id);
                table.CheckConstraint("CK_SalesCreditNotes_Amount", "[Amount] > 0");
                table.ForeignKey("FK_SalesCreditNotes_SalesInvoices_InvoiceId",
                    x => x.InvoiceId, "SalesInvoices", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_SalesCreditNotes_AspNetUsers_CreatedById",
                    x => x.CreatedById, "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });
        migrationBuilder.CreateTable("SalesRefunds",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                PaymentId = table.Column<long>(type: "bigint", nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                RefundedOn = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalesRefunds", x => x.Id);
                table.CheckConstraint("CK_SalesRefunds_Amount", "[Amount] > 0");
                table.CheckConstraint("CK_SalesRefunds_Method",
                    "[Method] IN (N'BankTransfer', N'UPI', N'Cash', N'Card', N'Other')");
                table.ForeignKey("FK_SalesRefunds_SalesInvoices_InvoiceId",
                    x => x.InvoiceId, "SalesInvoices", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_SalesRefunds_SalesPayments_PaymentId",
                    x => x.PaymentId, "SalesPayments", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_SalesRefunds_AspNetUsers_CreatedById",
                    x => x.CreatedById, "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });
        migrationBuilder.CreateIndex("IX_SalesCreditNotes_CreatedById", "SalesCreditNotes", "CreatedById");
        migrationBuilder.CreateIndex("IX_SalesCreditNotes_InvoiceId", "SalesCreditNotes", "InvoiceId");
        migrationBuilder.CreateIndex("IX_SalesRefunds_CreatedById", "SalesRefunds", "CreatedById");
        migrationBuilder.CreateIndex("IX_SalesRefunds_InvoiceId", "SalesRefunds", "InvoiceId");
        migrationBuilder.CreateIndex("IX_SalesRefunds_PaymentId", "SalesRefunds", "PaymentId");
        migrationBuilder.CreateIndex("IX_SalesRefunds_Reference", "SalesRefunds", "Reference", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("SalesCreditNotes");
        migrationBuilder.DropTable("SalesRefunds");
        migrationBuilder.DropCheckConstraint("CK_SalesInvoices_Status", "SalesInvoices");
        migrationBuilder.DropCheckConstraint("CK_SalesInvoices_Amounts", "SalesInvoices");
        migrationBuilder.DropCheckConstraint("CK_SalesPayments_Amount", "SalesPayments");
        migrationBuilder.DropColumn("CreditedAmount", "SalesInvoices");
        migrationBuilder.DropColumn("RefundedAmount", "SalesInvoices");
        migrationBuilder.DropColumn("RefundedAmount", "SalesPayments");
        migrationBuilder.AddCheckConstraint("CK_SalesInvoices_Status", "SalesInvoices",
            "[Status] IN (N'Issued', N'PartiallyPaid', N'Paid', N'Void')");
        migrationBuilder.AddCheckConstraint("CK_SalesInvoices_Amounts", "SalesInvoices",
            "[Amount] > 0 AND [PaidAmount] >= 0 AND [PaidAmount] <= [Amount]");
        migrationBuilder.AddCheckConstraint("CK_SalesPayments_Amount", "SalesPayments", "[Amount] > 0");
    }
}
