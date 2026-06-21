using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraMtgApi.Pricing.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPricesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "prices");

            migrationBuilder.CreateTable(
                name: "card_price_points",
                schema: "prices",
                columns: table => new
                {
                    PrintingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ObservedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Eur = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    EurFoil = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_price_points", x => new { x.PrintingId, x.ObservedOn });
                });

            migrationBuilder.CreateTable(
                name: "card_prices_latest",
                schema: "prices",
                columns: table => new
                {
                    PrintingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Eur = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    EurFoil = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_prices_latest", x => x.PrintingId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_card_price_points_PrintingId",
                schema: "prices",
                table: "card_price_points",
                column: "PrintingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_price_points",
                schema: "prices");

            migrationBuilder.DropTable(
                name: "card_prices_latest",
                schema: "prices");
        }
    }
}
