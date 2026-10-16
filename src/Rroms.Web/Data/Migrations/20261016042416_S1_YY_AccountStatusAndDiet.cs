using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Rroms.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class S1_YY_AccountStatusAndDiet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiningPreferences",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<DateTime>(
                name: "DietaryConsentAtUtc",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DietaryTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DietaryTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAvoidedDietaryTags",
                columns: table => new
                {
                    DietaryTagId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAvoidedDietaryTags", x => new { x.DietaryTagId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CustomerAvoidedDietaryTags_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerAvoidedDietaryTags_DietaryTags_DietaryTagId",
                        column: x => x.DietaryTagId,
                        principalTable: "DietaryTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "DietaryTags",
                columns: new[] { "Id", "DisplayOrder", "Name" },
                values: new object[,]
                {
                    { 1, 1, "Animal products" },
                    { 2, 2, "Dairy" },
                    { 3, 3, "Spicy" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAvoidedDietaryTags_UserId",
                table: "CustomerAvoidedDietaryTags",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DietaryTags_Name",
                table: "DietaryTags",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerAvoidedDietaryTags");

            migrationBuilder.DropTable(
                name: "DietaryTags");

            migrationBuilder.DropColumn(
                name: "DietaryConsentAtUtc",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<string>(
                name: "DiningPreferences",
                table: "AspNetUsers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
