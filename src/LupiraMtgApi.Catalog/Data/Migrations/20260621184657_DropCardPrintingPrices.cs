using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraMtgApi.Catalog.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropCardPrintingPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Prices",
                schema: "cards",
                table: "card_printings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Dictionary<string, decimal>>(
                name: "Prices",
                schema: "cards",
                table: "card_printings",
                type: "jsonb",
                nullable: true);
        }
    }
}
