using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace creaturegame.DB.Migrations.Items
{
    /// <inheritdoc />
    public partial class InitialCreateItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Cost = table.Column<int>(type: "INTEGER", nullable: false),
                    FlingPower = table.Column<int>(type: "INTEGER", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    SpriteUrl = table.Column<string>(type: "TEXT", nullable: true),
                    HealAmount = table.Column<int>(type: "INTEGER", nullable: true),
                    HealsAllHp = table.Column<bool>(type: "INTEGER", nullable: false),
                    CuresAllStatus = table.Column<bool>(type: "INTEGER", nullable: false),
                    CuredStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    RevivePercent = table.Column<int>(type: "INTEGER", nullable: true),
                    PpRestoreAmount = table.Column<int>(type: "INTEGER", nullable: true),
                    RestoresAllPp = table.Column<bool>(type: "INTEGER", nullable: false),
                    StatBoostStat = table.Column<int>(type: "INTEGER", nullable: true),
                    StatBoostStages = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Items");
        }
    }
}
