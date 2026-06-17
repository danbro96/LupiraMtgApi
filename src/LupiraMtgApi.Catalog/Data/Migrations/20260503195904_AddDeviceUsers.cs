using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraMtgApi.Catalog.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "me_devices",
                schema: "cards",
                columns: table => new
                {
                    Sub = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_me_devices", x => x.Sub);
                });

            migrationBuilder.CreateIndex(
                name: "IX_me_devices_TokenHash",
                schema: "cards",
                table: "me_devices",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "me_devices",
                schema: "cards");
        }
    }
}
