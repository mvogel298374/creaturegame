using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace creaturegame.Migrations
{
    /// <inheritdoc />
    public partial class newver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PowerPointsCurrent",
                table: "Moves");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PowerPointsCurrent",
                table: "Moves",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
