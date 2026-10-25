using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rroms.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class S4_Alex_KitchenControlsAndChatInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "QueuedSinceUtc",
                table: "KitchenTickets",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "ServedAtUtc",
                table: "KitchenTickets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatQuestionLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Topic = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AskedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatQuestionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KitchenStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    IsPaused = table.Column<bool>(type: "bit", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitchenStatuses", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "KitchenStatuses",
                columns: new[] { "Id", "ChangedAtUtc", "IsPaused" },
                values: new object[] { 1, new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), false });

            migrationBuilder.CreateIndex(
                name: "IX_ChatQuestionLogs_AskedAtUtc",
                table: "ChatQuestionLogs",
                column: "AskedAtUtc");

            // Tickets from before this change have no queue time; using the cooking start gives them zero wait
            // instead of a wait measured from the year 1.
            migrationBuilder.Sql("UPDATE KitchenTickets SET QueuedSinceUtc = StartedAtUtc;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatQuestionLogs");

            migrationBuilder.DropTable(
                name: "KitchenStatuses");

            migrationBuilder.DropColumn(
                name: "QueuedSinceUtc",
                table: "KitchenTickets");

            migrationBuilder.DropColumn(
                name: "ServedAtUtc",
                table: "KitchenTickets");
        }
    }
}
