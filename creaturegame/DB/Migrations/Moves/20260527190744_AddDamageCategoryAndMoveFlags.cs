using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace creaturegame.DB.Migrations.Moves
{
    /// <inheritdoc />
    public partial class AddDamageCategoryAndMoveFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DamageCategory",
                table: "Moves",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DrainPercent",
                table: "Moves",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<int>(
                name: "FixedDamageValue",
                table: "Moves",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeverMisses",
                table: "Moves",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DamageCategory",
                table: "Moves");

            migrationBuilder.DropColumn(
                name: "DrainPercent",
                table: "Moves");

            migrationBuilder.DropColumn(
                name: "FixedDamageValue",
                table: "Moves");

            migrationBuilder.DropColumn(
                name: "NeverMisses",
                table: "Moves");
        }
    }
}
