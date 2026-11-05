using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bob1.Modules.Carpool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DriverUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DriverFamilyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Seats = table.Column<int>(type: "INTEGER", nullable: false),
                    SeatsTaken = table.Column<int>(type: "INTEGER", nullable: false),
                    DepartureUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MeetingPoint = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsCancelled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ride_bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RideId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FamilyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Seats = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ride_bookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ride_bookings_rides_RideId",
                        column: x => x.RideId,
                        principalTable: "rides",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ride_bookings_RideId_UserId",
                table: "ride_bookings",
                columns: new[] { "RideId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rides_MatchId",
                table: "rides",
                column: "MatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ride_bookings");

            migrationBuilder.DropTable(
                name: "rides");
        }
    }
}
