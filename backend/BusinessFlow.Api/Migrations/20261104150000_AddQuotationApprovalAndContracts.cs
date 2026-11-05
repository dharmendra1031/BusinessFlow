using BusinessFlow.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104150000_AddQuotationApprovalAndContracts")]
public partial class AddQuotationApprovalAndContracts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_SalesQuotations_Status", "SalesQuotations");

        migrationBuilder.AddColumn<DateTime>("ApprovalRequestedAtUtc", "SalesQuotations", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<long>("ApprovedById", "SalesQuotations", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<DateTime>("ApprovedAtUtc", "SalesQuotations", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<string>("ApprovalComment", "SalesQuotations", type: "nvarchar(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<DateTime>("SentAtUtc", "SalesQuotations", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<string>("ClientResponseBy", "SalesQuotations", type: "nvarchar(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<string>("ClientResponseReference", "SalesQuotations", type: "nvarchar(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<DateTime>("ClientRespondedAtUtc", "SalesQuotations", type: "datetime2", nullable: true);

        migrationBuilder.AddCheckConstraint("CK_SalesQuotations_Status", "SalesQuotations",
            "[Status] IN (N'Draft', N'PendingApproval', N'Approved', N'Sent', N'Accepted', N'Rejected')");
        migrationBuilder.CreateIndex("IX_SalesQuotations_ApprovedById", "SalesQuotations", "ApprovedById");
        migrationBuilder.AddForeignKey("FK_SalesQuotations_AspNetUsers_ApprovedById", "SalesQuotations",
            "ApprovedById", "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.NoAction);

        migrationBuilder.CreateTable(
            name: "SalesContracts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                QuotationId = table.Column<long>(type: "bigint", nullable: false),
                QuotationRevision = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Terms = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                SignedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                SignatureReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                SignedOn = table.Column<DateOnly>(type: "date", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalesContracts", x => x.Id);
                table.CheckConstraint("CK_SalesContracts_Status", "[Status] IN (N'Draft', N'Signed')");
                table.ForeignKey("FK_SalesContracts_SalesQuotations_QuotationId",
                    x => x.QuotationId, "SalesQuotations", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_SalesContracts_AspNetUsers_CreatedById",
                    x => x.CreatedById, "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex("IX_SalesContracts_QuotationId", "SalesContracts",
            "QuotationId", unique: true);
        migrationBuilder.CreateIndex("IX_SalesContracts_CreatedById", "SalesContracts", "CreatedById");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("SalesContracts");
        migrationBuilder.DropForeignKey("FK_SalesQuotations_AspNetUsers_ApprovedById", "SalesQuotations");
        migrationBuilder.DropIndex("IX_SalesQuotations_ApprovedById", "SalesQuotations");
        migrationBuilder.DropCheckConstraint("CK_SalesQuotations_Status", "SalesQuotations");
        migrationBuilder.AddCheckConstraint("CK_SalesQuotations_Status", "SalesQuotations",
            "[Status] IN (N'Draft', N'Sent', N'Accepted', N'Rejected')");
        migrationBuilder.DropColumn("ApprovalRequestedAtUtc", "SalesQuotations");
        migrationBuilder.DropColumn("ApprovedById", "SalesQuotations");
        migrationBuilder.DropColumn("ApprovedAtUtc", "SalesQuotations");
        migrationBuilder.DropColumn("ApprovalComment", "SalesQuotations");
        migrationBuilder.DropColumn("SentAtUtc", "SalesQuotations");
        migrationBuilder.DropColumn("ClientResponseBy", "SalesQuotations");
        migrationBuilder.DropColumn("ClientResponseReference", "SalesQuotations");
        migrationBuilder.DropColumn("ClientRespondedAtUtc", "SalesQuotations");
    }
}
