using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rroms.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MenuDiscounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Percent = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DaysOfWeek = table.Column<int>(type: "int", nullable: false),
                    FromMinute = table.Column<int>(type: "int", nullable: true),
                    ToMinute = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuDiscounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MenuDiscountCategories",
                columns: table => new
                {
                    CategoriesId = table.Column<int>(type: "int", nullable: false),
                    MenuDiscountId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuDiscountCategories", x => new { x.CategoriesId, x.MenuDiscountId });
                    table.ForeignKey(
                        name: "FK_MenuDiscountCategories_MenuCategories_CategoriesId",
                        column: x => x.CategoriesId,
                        principalTable: "MenuCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MenuDiscountCategories_MenuDiscounts_MenuDiscountId",
                        column: x => x.MenuDiscountId,
                        principalTable: "MenuDiscounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MenuDiscountDishes",
                columns: table => new
                {
                    DishesId = table.Column<int>(type: "int", nullable: false),
                    MenuDiscountId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuDiscountDishes", x => new { x.DishesId, x.MenuDiscountId });
                    table.ForeignKey(
                        name: "FK_MenuDiscountDishes_MenuDiscounts_MenuDiscountId",
                        column: x => x.MenuDiscountId,
                        principalTable: "MenuDiscounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MenuDiscountDishes_MenuItems_DishesId",
                        column: x => x.DishesId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MenuDiscountCategories_MenuDiscountId",
                table: "MenuDiscountCategories",
                column: "MenuDiscountId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuDiscountDishes_MenuDiscountId",
                table: "MenuDiscountDishes",
                column: "MenuDiscountId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuDiscounts_IsActive",
                table: "MenuDiscounts",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MenuDiscountCategories");

            migrationBuilder.DropTable(
                name: "MenuDiscountDishes");

            migrationBuilder.DropTable(
                name: "MenuDiscounts");
        }
    }
}
