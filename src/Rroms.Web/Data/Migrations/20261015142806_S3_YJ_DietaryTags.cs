using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rroms.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class S3_YJ_DietaryTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSpicy",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "IsVegetarian",
                table: "MenuItems");
        }
    }
}
