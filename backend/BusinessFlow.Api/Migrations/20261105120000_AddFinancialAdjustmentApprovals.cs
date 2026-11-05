using BusinessFlow.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261105120000_AddFinancialAdjustmentApprovals")]
public partial class AddFinancialAdjustmentApprovals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FinancialAdjustmentApprovals",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                PaymentId = table.Column<long>(type: "bigint", nullable: true),
                Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                InvoiceVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                State = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                RequestedById = table.Column<long>(type: "bigint", nullable: false),
                RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                DecidedById = table.Column<long>(type: "bigint", nullable: true),
                DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                DecisionComment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FinancialAdjustmentApprovals", x => x.Id);
                table.CheckConstraint("CK_FinancialAdjustmentApprovals_Amount", "[Amount] > 0");
                table.CheckConstraint("CK_FinancialAdjustmentApprovals_Kind", "[Kind] IN (N'Credit', N'Refund')");
                table.CheckConstraint("CK_FinancialAdjustmentApprovals_State", "[State] IN (N'Pending', N'Approved', N'Rejected')");
                table.ForeignKey("FK_FinancialAdjustmentApprovals_SalesInvoices_InvoiceId",
                    x => x.InvoiceId, "SalesInvoices", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_FinancialAdjustmentApprovals_SalesPayments_PaymentId",
                    x => x.PaymentId, "SalesPayments", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_FinancialAdjustmentApprovals_AspNetUsers_RequestedById",
                    x => x.RequestedById, "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_FinancialAdjustmentApprovals_AspNetUsers_DecidedById",
                    x => x.DecidedById, "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });
        migrationBuilder.CreateIndex("IX_FinancialAdjustmentApprovals_InvoiceId_Kind",
            "FinancialAdjustmentApprovals", new[] { "InvoiceId", "Kind" },
            unique: true, filter: "[State] = N'Pending'");
        migrationBuilder.CreateIndex("IX_FinancialAdjustmentApprovals_PaymentId", "FinancialAdjustmentApprovals", "PaymentId");
        migrationBuilder.CreateIndex("IX_FinancialAdjustmentApprovals_RequestedById", "FinancialAdjustmentApprovals", "RequestedById");
        migrationBuilder.CreateIndex("IX_FinancialAdjustmentApprovals_DecidedById", "FinancialAdjustmentApprovals", "DecidedById");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("FinancialAdjustmentApprovals");
}
