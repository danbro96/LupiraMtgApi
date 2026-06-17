using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraMtgApi.Catalog.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFullCardPHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FullCardPHash",
                schema: "cards",
                table: "card_printings",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FullCardPHash",
                schema: "cards",
                table: "card_printings");
        }
    }
}
