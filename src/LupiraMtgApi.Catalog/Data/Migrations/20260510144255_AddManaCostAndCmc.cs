using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraMtgApi.Catalog.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManaCostAndCmc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "Cmc",
                schema: "cards",
                table: "card_printings",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManaCost",
                schema: "cards",
                table: "card_printings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_card_printings_Cmc",
                schema: "cards",
                table: "card_printings",
                column: "Cmc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_card_printings_Cmc",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropColumn(
                name: "Cmc",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropColumn(
                name: "ManaCost",
                schema: "cards",
                table: "card_printings");
        }
    }
}
