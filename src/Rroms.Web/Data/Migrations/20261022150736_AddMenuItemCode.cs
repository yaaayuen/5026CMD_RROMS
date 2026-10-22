using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rroms.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuItemCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "MenuItems",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.Sql(@"
                WITH numbered AS (
                    SELECT i.Id,
                           UPPER(LEFT(LTRIM(c.Name), 1)) AS Prefix,
                           ROW_NUMBER() OVER (PARTITION BY UPPER(LEFT(LTRIM(c.Name), 1)) ORDER BY i.Id) AS Seq
                    FROM MenuItems i
                    JOIN MenuCategories c ON c.Id = i.MenuCategoryId
                )
                UPDATE i
                SET i.Code = n.Prefix + CASE WHEN n.Seq < 10 THEN '0' + CAST(n.Seq AS varchar(10)) ELSE CAST(n.Seq AS varchar(10)) END
                FROM MenuItems i
                JOIN numbered n ON n.Id = i.Id;");


            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_Code",
                table: "MenuItems",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MenuItems_Code",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "MenuItems");
        }
    }
}
