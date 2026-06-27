using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace creaturegame.DB.Migrations.Pokemon
{
    /// <inheritdoc />
    public partial class AddLearnsetMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Method",
                table: "PokemonLearnset",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Method",
                table: "PokemonLearnset");
        }
    }
}
