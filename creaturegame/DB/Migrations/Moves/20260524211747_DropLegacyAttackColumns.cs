using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace creaturegame.DB.Migrations.Moves
{
    /// <inheritdoc />
    public partial class DropLegacyAttackColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cooldown",
                table: "Moves");

            migrationBuilder.DropColumn(
                name: "CriticalChance",
                table: "Moves");

            migrationBuilder.DropColumn(
                name: "DamageVariance",
                table: "Moves");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "Moves");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Cooldown",
                table: "Moves",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "CriticalChance",
                table: "Moves",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "DamageVariance",
                table: "Moves",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "Weight",
                table: "Moves",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
