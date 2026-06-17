using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraMtgApi.Catalog.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSetIconFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IconObjectKey",
                schema: "cards",
                table: "sets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "IconPHash",
                schema: "cards",
                table: "sets",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IconSyncedAt",
                schema: "cards",
                table: "sets",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IconObjectKey",
                schema: "cards",
                table: "sets");

            migrationBuilder.DropColumn(
                name: "IconPHash",
                schema: "cards",
                table: "sets");

            migrationBuilder.DropColumn(
                name: "IconSyncedAt",
                schema: "cards",
                table: "sets");
        }
    }
}
