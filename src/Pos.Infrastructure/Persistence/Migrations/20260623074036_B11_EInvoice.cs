using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B11_EInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceSerial",
                table: "Stores",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InvoiceTemplateCode",
                table: "Stores",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "EInvoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IssuedAt",
                table: "EInvoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderRef",
                table: "EInvoices",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceSerial",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "InvoiceTemplateCode",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "EInvoices");

            migrationBuilder.DropColumn(
                name: "IssuedAt",
                table: "EInvoices");

            migrationBuilder.DropColumn(
                name: "ProviderRef",
                table: "EInvoices");
        }
    }
}
