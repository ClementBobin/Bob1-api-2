using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bob1.Participation.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "child_registrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChildId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FamilyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_child_registrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "presence_confirmations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FamilyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConfirmedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_presence_confirmations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reminder_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reminder_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "slots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    Filled = table.Column<int>(type: "INTEGER", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "slot_signups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SlotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FamilyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slot_signups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_slot_signups_slots_SlotId",
                        column: x => x.SlotId,
                        principalTable: "slots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_child_registrations_FamilyId",
                table: "child_registrations",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_child_registrations_MatchId_ChildId",
                table: "child_registrations",
                columns: new[] { "MatchId", "ChildId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_presence_confirmations_MatchId_FamilyId",
                table: "presence_confirmations",
                columns: new[] { "MatchId", "FamilyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reminder_logs_Key",
                table: "reminder_logs",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_slot_signups_SlotId_UserId",
                table: "slot_signups",
                columns: new[] { "SlotId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_slot_signups_UserId",
                table: "slot_signups",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_slots_MatchId",
                table: "slots",
                column: "MatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "child_registrations");

            migrationBuilder.DropTable(
                name: "presence_confirmations");

            migrationBuilder.DropTable(
                name: "reminder_logs");

            migrationBuilder.DropTable(
                name: "slot_signups");

            migrationBuilder.DropTable(
                name: "slots");
        }
    }
}
