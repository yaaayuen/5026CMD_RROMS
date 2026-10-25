using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rroms.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class S4_Alex_TableRequestsDishRatingsAndRecurringPosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "DailyFromLocal",
                table: "Announcements",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DailyUntilLocal",
                table: "Announcements",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepeatDays",
                table: "Announcements",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DishRatings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedbackId = table.Column<int>(type: "int", nullable: false),
                    MenuItemId = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DishRatings", x => x.Id);
                    table.CheckConstraint("CK_DishRatings_Rating", "[Rating] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_DishRatings_Feedback_FeedbackId",
                        column: x => x.FeedbackId,
                        principalTable: "Feedback",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DishRatings_MenuItems_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TableRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReservationId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HandledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TableRequests_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DishRatings_FeedbackId_MenuItemId",
                table: "DishRatings",
                columns: new[] { "FeedbackId", "MenuItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DishRatings_MenuItemId",
                table: "DishRatings",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_TableRequests_ReservationId_HandledAtUtc",
                table: "TableRequests",
                columns: new[] { "ReservationId", "HandledAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DishRatings");

            migrationBuilder.DropTable(
                name: "TableRequests");

            migrationBuilder.DropColumn(
                name: "DailyFromLocal",
                table: "Announcements");

            migrationBuilder.DropColumn(
                name: "DailyUntilLocal",
                table: "Announcements");

            migrationBuilder.DropColumn(
                name: "RepeatDays",
                table: "Announcements");
        }
    }
}
