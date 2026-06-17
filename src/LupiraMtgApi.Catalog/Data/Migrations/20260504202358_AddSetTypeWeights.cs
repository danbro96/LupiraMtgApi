using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraMtgApi.Catalog.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSetTypeWeights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "set_type_weights",
                schema: "cards",
                columns: table => new
                {
                    SetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_set_type_weights", x => x.SetType);
                });

            var seededAt = new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero);
            migrationBuilder.InsertData(
                schema: "cards",
                table: "set_type_weights",
                columns: new[] { "SetType", "Weight", "UpdatedAt" },
                values: new object[,]
                {
                    { "expansion", 1.00, seededAt },
                    { "core", 1.00, seededAt },
                    { "draft_innovation", 0.95, seededAt },
                    { "masters", 0.90, seededAt },
                    { "commander", 0.75, seededAt },
                    { "duel_deck", 0.65, seededAt },
                    { "starter", 0.60, seededAt },
                    { "from_the_vault", 0.50, seededAt },
                    { "box", 0.50, seededAt },
                    { "masterpiece", 0.50, seededAt },
                    { "archenemy", 0.50, seededAt },
                    { "planechase", 0.50, seededAt },
                    { "spellbook", 0.45, seededAt },
                    { "premium_deck", 0.45, seededAt },
                    { "eternal", 0.40, seededAt },
                    { "alchemy", 0.35, seededAt },
                    { "promo", 0.35, seededAt },
                    { "treasure_chest", 0.20, seededAt },
                    { "vanguard", 0.20, seededAt },
                    { "arsenal", 0.20, seededAt },
                    { "funny", 0.15, seededAt },
                    { "token", 0.10, seededAt },
                    { "minigame", 0.10, seededAt },
                    { "memorabilia", 0.10, seededAt },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "set_type_weights",
                schema: "cards");
        }
    }
}
