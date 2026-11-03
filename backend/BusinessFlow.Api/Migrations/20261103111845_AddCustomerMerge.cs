using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MergedAtUtc",
                table: "Customers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MergedIntoCustomerId",
                table: "Customers",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_MergedIntoCustomerId",
                table: "Customers",
                column: "MergedIntoCustomerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Customers_MergedIntoCustomerId",
                table: "Customers",
                column: "MergedIntoCustomerId",
                principalTable: "Customers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Customers_MergedIntoCustomerId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_MergedIntoCustomerId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MergedAtUtc",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MergedIntoCustomerId",
                table: "Customers");
        }
    }
}
