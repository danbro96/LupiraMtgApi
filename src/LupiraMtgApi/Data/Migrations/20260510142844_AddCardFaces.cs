using System.Collections.Generic;
using LupiraMtgApi.Data.Entities;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraMtgApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCardFaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<CardFace>>(
                name: "Faces",
                schema: "cards",
                table: "card_printings",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Faces",
                schema: "cards",
                table: "card_printings");
        }
    }
}
