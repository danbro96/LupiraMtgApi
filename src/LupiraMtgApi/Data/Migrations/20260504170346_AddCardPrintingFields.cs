using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraMtgApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCardPrintingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFoil",
                schema: "cards",
                table: "card_printings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Lang",
                schema: "cards",
                table: "card_printings",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string>(
                name: "Layout",
                schema: "cards",
                table: "card_printings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "normal");

            migrationBuilder.AddColumn<string>(
                name: "OracleText",
                schema: "cards",
                table: "card_printings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Power",
                schema: "cards",
                table: "card_printings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RulesText",
                schema: "cards",
                table: "card_printings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subtype",
                schema: "cards",
                table: "card_printings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Supertype",
                schema: "cards",
                table: "card_printings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Toughness",
                schema: "cards",
                table: "card_printings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                schema: "cards",
                table: "card_printings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TypeLineFull",
                schema: "cards",
                table: "card_printings",
                type: "text",
                nullable: true,
                computedColumnSql: "NULLIF(TRIM(BOTH ' ' FROM\n    COALESCE(\"Supertype\" || ' ', '')\n    || COALESCE(\"Type\", '')\n    || CASE WHEN \"Subtype\" IS NULL THEN '' ELSE ' — ' || \"Subtype\" END\n), '')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_card_printings_RulesText",
                schema: "cards",
                table: "card_printings",
                column: "RulesText")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_card_printings_SetCode_CollectorNumber_Lang",
                schema: "cards",
                table: "card_printings",
                columns: new[] { "SetCode", "CollectorNumber", "Lang" });

            migrationBuilder.CreateIndex(
                name: "IX_card_printings_TypeLineFull",
                schema: "cards",
                table: "card_printings",
                column: "TypeLineFull")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_card_printings_RulesText",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropIndex(
                name: "IX_card_printings_SetCode_CollectorNumber_Lang",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropIndex(
                name: "IX_card_printings_TypeLineFull",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropColumn(
                name: "TypeLineFull",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropColumn(
                name: "IsFoil",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropColumn(
                name: "Lang",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropColumn(
                name: "Layout",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropColumn(
                name: "OracleText",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropColumn(
                name: "Power",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropColumn(
                name: "RulesText",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropColumn(
                name: "Subtype",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropColumn(
                name: "Supertype",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropColumn(
                name: "Toughness",
                schema: "cards",
                table: "card_printings");

            migrationBuilder.DropColumn(
                name: "Type",
                schema: "cards",
                table: "card_printings");
        }
    }
}
