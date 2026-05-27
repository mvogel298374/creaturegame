using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace creaturegame.DB.Migrations.Moves
{
    /// <inheritdoc />
    public partial class AddStatEffectAndMoveEffect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Effect",
                table: "Moves",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StatEffectChance",
                table: "Moves",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatEffectDelta",
                table: "Moves",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatEffectStat",
                table: "Moves",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatEffectTarget",
                table: "Moves",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Effect",
                table: "Moves");

            migrationBuilder.DropColumn(
                name: "StatEffectChance",
                table: "Moves");

            migrationBuilder.DropColumn(
                name: "StatEffectDelta",
                table: "Moves");

            migrationBuilder.DropColumn(
                name: "StatEffectStat",
                table: "Moves");

            migrationBuilder.DropColumn(
                name: "StatEffectTarget",
                table: "Moves");
        }
    }
}
