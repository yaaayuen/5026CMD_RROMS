using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rroms.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class S4_Alex_FeedbackRepliesAndScheduledOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CommentRemoved",
                table: "Feedback",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFlagged",
                table: "Feedback",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "Feedback",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepliedAtUtc",
                table: "Feedback",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reply",
                table: "Feedback",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MenuItemId",
                table: "Announcements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotifiedAtUtc",
                table: "Announcements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BlockedWords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Word = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedWords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_MenuItemId",
                table: "Announcements",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockedWords_Word",
                table: "BlockedWords",
                column: "Word",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Announcements_MenuItems_MenuItemId",
                table: "Announcements",
                column: "MenuItemId",
                principalTable: "MenuItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Promotions that were published before this change already notified customers when saved.
            // Marking them stops AnnouncementScheduler from sending the same promotion a second time.
            migrationBuilder.Sql(
                "UPDATE Announcements SET NotifiedAtUtc = UpdatedAtUtc WHERE IsPromotion = 1 AND IsPublished = 1 AND StartUtc <= UpdatedAtUtc;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Announcements_MenuItems_MenuItemId",
                table: "Announcements");

            migrationBuilder.DropTable(
                name: "BlockedWords");

            migrationBuilder.DropIndex(
                name: "IX_Announcements_MenuItemId",
                table: "Announcements");

            migrationBuilder.DropColumn(
                name: "CommentRemoved",
                table: "Feedback");

            migrationBuilder.DropColumn(
                name: "IsFlagged",
                table: "Feedback");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "Feedback");

            migrationBuilder.DropColumn(
                name: "RepliedAtUtc",
                table: "Feedback");

            migrationBuilder.DropColumn(
                name: "Reply",
                table: "Feedback");

            migrationBuilder.DropColumn(
                name: "MenuItemId",
                table: "Announcements");

            migrationBuilder.DropColumn(
                name: "NotifiedAtUtc",
                table: "Announcements");
        }
    }
}
