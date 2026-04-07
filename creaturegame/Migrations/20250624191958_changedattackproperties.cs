using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace creaturegame.Migrations
{
    /// <inheritdoc />
    public partial class changedattackproperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttackType",
                table: "Moves",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttackType",
                table: "Moves");
        }
    }
}
