using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rroms.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class S3_YJ_UniqueDishName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_Name",
                table: "MenuItems",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MenuItems_Name",
                table: "MenuItems");
        }
    }
}
