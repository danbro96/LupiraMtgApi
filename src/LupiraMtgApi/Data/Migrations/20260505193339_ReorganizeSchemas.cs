using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraMtgApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReorganizeSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ensure all three new schemas exist before any cross-schema move.
            migrationBuilder.EnsureSchema(name: "auth");
            migrationBuilder.EnsureSchema(name: "users");
            migrationBuilder.EnsureSchema(name: "diagnostics");

            // Move identity table out of `cards` and into `auth`, renaming the
            // PK column Sub -> Id along the way. The 6 device rows are preserved.
            migrationBuilder.DropPrimaryKey(
                name: "PK_me_devices",
                schema: "cards",
                table: "me_devices");

            migrationBuilder.RenameTable(
                name: "me_devices",
                schema: "cards",
                newName: "devices",
                newSchema: "auth");

            migrationBuilder.RenameColumn(
                name: "Sub",
                schema: "auth",
                table: "devices",
                newName: "Id");

            migrationBuilder.RenameIndex(
                name: "IX_me_devices_TokenHash",
                schema: "auth",
                table: "devices",
                newName: "IX_devices_TokenHash");

            migrationBuilder.AddPrimaryKey(
                name: "PK_devices",
                schema: "auth",
                table: "devices",
                column: "Id");

            // Drop existing Marten doc tables in `public`. Per direction, all
            // user/collection/scan data is greenfield-discarded; Marten will
            // regenerate the event store + projection tables in `users` on next
            // write, and the scan log doc table in `diagnostics` per the per-doc
            // schema override in MartenRegistrations.
            // Helper functions in `public` (e.g. mt_immutable_timestamp) stay as
            // harmless orphans; Marten recreates equivalents in `users` /
            // `diagnostics` on first write.
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.mt_doc_collectiondocument CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.mt_doc_selectiondocument CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.mt_doc_scanlogdocument CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.mt_doc_userprofiledocument CASCADE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_devices",
                schema: "auth",
                table: "devices");

            migrationBuilder.RenameTable(
                name: "devices",
                schema: "auth",
                newName: "me_devices",
                newSchema: "cards");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "cards",
                table: "me_devices",
                newName: "Sub");

            migrationBuilder.RenameIndex(
                name: "IX_devices_TokenHash",
                schema: "cards",
                table: "me_devices",
                newName: "IX_me_devices_TokenHash");

            migrationBuilder.AddPrimaryKey(
                name: "PK_me_devices",
                schema: "cards",
                table: "me_devices",
                column: "Sub");
        }
    }
}
