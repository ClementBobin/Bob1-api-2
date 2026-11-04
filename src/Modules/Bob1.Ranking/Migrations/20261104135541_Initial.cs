using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bob1.Ranking.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "point_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Points = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Season = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_point_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ranking_rules",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Points = table.Column<int>(type: "INTEGER", nullable: false),
                    PointsJ15 = table.Column<int>(type: "INTEGER", nullable: true),
                    PointsJ4 = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ranking_rules", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_point_entries_Season_Category_PersonId",
                table: "point_entries",
                columns: new[] { "Season", "Category", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_point_entries_SourceKey",
                table: "point_entries",
                column: "SourceKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "point_entries");

            migrationBuilder.DropTable(
                name: "ranking_rules");
        }
    }
}
