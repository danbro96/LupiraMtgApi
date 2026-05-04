using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraMtgApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCardsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cards");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "card_printings",
                schema: "cards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OracleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SetCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CollectorNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ColorIdentity = table.Column<string[]>(type: "text[]", nullable: false),
                    Rarity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ImageObjectKey = table.Column<string>(type: "text", nullable: true),
                    ImageArtCropKey = table.Column<string>(type: "text", nullable: true),
                    ArtPHash = table.Column<long>(type: "bigint", nullable: true),
                    Prices = table.Column<Dictionary<string, decimal>>(type: "jsonb", nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_printings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sets",
                schema: "cards",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReleasedAt = table.Column<DateOnly>(type: "date", nullable: true),
                    CardCount = table.Column<int>(type: "integer", nullable: false),
                    IconSvgUri = table.Column<string>(type: "text", nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sets", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_card_printings_Name",
                schema: "cards",
                table: "card_printings",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_card_printings_OracleId",
                schema: "cards",
                table: "card_printings",
                column: "OracleId");

            migrationBuilder.CreateIndex(
                name: "IX_card_printings_SetCode_CollectorNumber",
                schema: "cards",
                table: "card_printings",
                columns: new[] { "SetCode", "CollectorNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_printings",
                schema: "cards");

            migrationBuilder.DropTable(
                name: "sets",
                schema: "cards");
        }
    }
}
