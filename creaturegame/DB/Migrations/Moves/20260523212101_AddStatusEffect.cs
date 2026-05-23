using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace creaturegame.DB.Migrations.Moves
{
    /// <inheritdoc />
    public partial class AddStatusEffect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StatusEffect",
                table: "Moves",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusEffect",
                table: "Moves");
        }
    }
}
