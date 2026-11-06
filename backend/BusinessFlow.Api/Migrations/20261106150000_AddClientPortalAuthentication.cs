using BusinessFlow.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261106150000_AddClientPortalAuthentication")]
public partial class AddClientPortalAuthentication : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ClientPortalAccounts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                CustomerId = table.Column<long>(type: "bigint", nullable: false),
                ContactId = table.Column<long>(type: "bigint", nullable: false),
                Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                SessionVersion = table.Column<int>(type: "int", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ClientPortalAccounts", x => x.Id);
                table.CheckConstraint("CK_ClientPortalAccounts_SessionVersion", "[SessionVersion] > 0");
                table.ForeignKey("FK_ClientPortalAccounts_Customers_CustomerId",
                    x => x.CustomerId, "Customers", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_ClientPortalAccounts_CustomerContacts_ContactId",
                    x => x.ContactId, "CustomerContacts", "Id", onDelete: ReferentialAction.NoAction);
                table.ForeignKey("FK_ClientPortalAccounts_AspNetUsers_CreatedById",
                    x => x.CreatedById, "AspNetUsers", "Id", onDelete: ReferentialAction.NoAction);
            });
        migrationBuilder.CreateIndex("IX_ClientPortalAccounts_ContactId",
            "ClientPortalAccounts", "ContactId", unique: true);
        migrationBuilder.CreateIndex("IX_ClientPortalAccounts_CustomerId",
            "ClientPortalAccounts", "CustomerId");
        migrationBuilder.CreateIndex("IX_ClientPortalAccounts_CreatedById",
            "ClientPortalAccounts", "CreatedById");
        migrationBuilder.CreateIndex("IX_ClientPortalAccounts_Email",
            "ClientPortalAccounts", "Email");

        migrationBuilder.CreateTable(
            name: "ClientLoginChallenges",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                AccountId = table.Column<long>(type: "bigint", nullable: false),
                CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                AttemptCount = table.Column<int>(type: "int", nullable: false),
                UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ClientLoginChallenges", x => x.Id);
                table.CheckConstraint("CK_ClientLoginChallenges_AttemptCount",
                    "[AttemptCount] BETWEEN 0 AND 5");
                table.ForeignKey("FK_ClientLoginChallenges_ClientPortalAccounts_AccountId",
                    x => x.AccountId, "ClientPortalAccounts", "Id",
                    onDelete: ReferentialAction.NoAction);
            });
        migrationBuilder.CreateIndex("IX_ClientLoginChallenges_AccountId_CreatedAtUtc",
            "ClientLoginChallenges", new[] { "AccountId", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ClientLoginChallenges");
        migrationBuilder.DropTable("ClientPortalAccounts");
    }
}
