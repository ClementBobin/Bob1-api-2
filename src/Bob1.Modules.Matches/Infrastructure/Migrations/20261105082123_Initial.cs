using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bob1.Modules.Matches.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DivisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DivisionName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Opponent = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsHome = table.Column<bool>(type: "INTEGER", nullable: false),
                    VenueId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VenueName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    HomeScore = table.Column<int>(type: "INTEGER", nullable: true),
                    AwayScore = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_DivisionId",
                table: "matches",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_matches_StartsAtUtc",
                table: "matches",
                column: "StartsAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "matches");
        }
    }
}
