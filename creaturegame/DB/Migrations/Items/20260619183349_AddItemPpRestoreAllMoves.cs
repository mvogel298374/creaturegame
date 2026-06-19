using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace creaturegame.DB.Migrations.Items
{
    /// <inheritdoc />
    public partial class AddItemPpRestoreAllMoves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RestoresPpAllMoves",
                table: "Items",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RestoresPpAllMoves",
                table: "Items");
        }
    }
}
