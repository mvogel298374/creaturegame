using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace creaturegame.DB.Migrations.Pokemon
{
    /// <inheritdoc />
    public partial class AddPokemonEvolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PokemonEvolution",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FromSpeciesId = table.Column<int>(type: "INTEGER", nullable: false),
                    ToSpeciesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Trigger = table.Column<int>(type: "INTEGER", nullable: false),
                    LevelThreshold = table.Column<int>(type: "INTEGER", nullable: true),
                    StoneItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    Generation = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PokemonEvolution", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PokemonEvolution_PokemonSpecies_FromSpeciesId",
                        column: x => x.FromSpeciesId,
                        principalTable: "PokemonSpecies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PokemonEvolution_FromSpeciesId_Generation",
                table: "PokemonEvolution",
                columns: new[] { "FromSpeciesId", "Generation" });

            migrationBuilder.CreateIndex(
                name: "IX_PokemonEvolution_FromSpeciesId_ToSpeciesId_Generation",
                table: "PokemonEvolution",
                columns: new[] { "FromSpeciesId", "ToSpeciesId", "Generation" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PokemonEvolution");
        }
    }
}
