using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rroms.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class S3_YJ_DietaryTagsOnMenuItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MenuItemDietaryTags",
                columns: table => new
                {
                    DietaryTagId = table.Column<int>(type: "int", nullable: false),
                    MenuItemId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItemDietaryTags", x => new { x.DietaryTagId, x.MenuItemId });
                    table.ForeignKey(
                        name: "FK_MenuItemDietaryTags_DietaryTags_DietaryTagId",
                        column: x => x.DietaryTagId,
                        principalTable: "DietaryTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MenuItemDietaryTags_MenuItems_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemDietaryTags_MenuItemId",
                table: "MenuItemDietaryTags",
                column: "MenuItemId");

            // Keep the old tags: a dish that was not vegetarian now contains animal products; spicy stays spicy.
            migrationBuilder.Sql("INSERT INTO MenuItemDietaryTags (DietaryTagId, MenuItemId) SELECT 1, Id FROM MenuItems WHERE IsVegetarian = 0");
            migrationBuilder.Sql("INSERT INTO MenuItemDietaryTags (DietaryTagId, MenuItemId) SELECT 3, Id FROM MenuItems WHERE IsSpicy = 1");

            // Only after the copy are the old columns dropped.
            migrationBuilder.DropColumn(
                name: "IsSpicy",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "IsVegetarian",
                table: "MenuItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MenuItemDietaryTags");

            migrationBuilder.AddColumn<bool>(
                name: "IsSpicy",
                table: "MenuItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVegetarian",
                table: "MenuItems",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
