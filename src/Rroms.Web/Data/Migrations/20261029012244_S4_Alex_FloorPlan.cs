using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Rroms.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class S4_Alex_FloorPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FloorAreas",
                columns: table => new
                {
                    DiningAreaId = table.Column<int>(type: "int", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FloorAreas", x => x.DiningAreaId);
                    table.CheckConstraint("CK_FloorAreas_Size", "[Width] BETWEEN 8 AND 60 AND [Height] BETWEEN 8 AND 60");
                    table.ForeignKey(
                        name: "FK_FloorAreas_DiningAreas_DiningAreaId",
                        column: x => x.DiningAreaId,
                        principalTable: "DiningAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FloorFeatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DiningAreaId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    X = table.Column<int>(type: "int", nullable: false),
                    Y = table.Column<int>(type: "int", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FloorFeatures", x => x.Id);
                    table.CheckConstraint("CK_FloorFeatures_Size", "[X] >= 0 AND [Y] >= 0 AND [Width] >= 0 AND [Height] >= 0");
                    table.ForeignKey(
                        name: "FK_FloorFeatures_DiningAreas_DiningAreaId",
                        column: x => x.DiningAreaId,
                        principalTable: "DiningAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TablePlacements",
                columns: table => new
                {
                    DiningTableId = table.Column<int>(type: "int", nullable: false),
                    X = table.Column<int>(type: "int", nullable: false),
                    Y = table.Column<int>(type: "int", nullable: false),
                    Rotation = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TablePlacements", x => x.DiningTableId);
                    table.CheckConstraint("CK_TablePlacements_Position", "[X] >= 0 AND [Y] >= 0");
                    table.CheckConstraint("CK_TablePlacements_Rotation", "[Rotation] IN (0, 90)");
                    table.ForeignKey(
                        name: "FK_TablePlacements_DiningTables_DiningTableId",
                        column: x => x.DiningTableId,
                        principalTable: "DiningTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "FloorAreas",
                columns: new[] { "DiningAreaId", "Height", "Width" },
                values: new object[,]
                {
                    { 1, 15, 24 },
                    { 2, 10, 16 }
                });

            migrationBuilder.InsertData(
                table: "FloorFeatures",
                columns: new[] { "Id", "DiningAreaId", "Height", "Kind", "Label", "Width", "X", "Y" },
                values: new object[,]
                {
                    { 1, 1, 8, "Block", "Bar", 3, 0, 0 },
                    { 2, 1, 5, "Block", "Kitchen", 6, 18, 0 },
                    { 3, 1, 0, "Window", "Windows", 14, 3, 0 },
                    { 4, 1, 0, "Door", "Entrance", 4, 10, 15 },
                    { 5, 1, 3, "Door", "To patio", 0, 24, 9 },
                    { 6, 2, 1, "Block", "Planters", 16, 0, 0 },
                    { 7, 2, 3, "Door", "From hall", 0, 0, 4 }
                });

            migrationBuilder.InsertData(
                table: "TablePlacements",
                columns: new[] { "DiningTableId", "Rotation", "X", "Y" },
                values: new object[,]
                {
                    { 1, 0, 3, 11 },
                    { 2, 0, 7, 11 },
                    { 3, 0, 4, 2 },
                    { 4, 0, 9, 2 },
                    { 5, 0, 12, 7 },
                    { 6, 0, 18, 11 },
                    { 7, 0, 3, 3 },
                    { 8, 0, 8, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_FloorFeatures_DiningAreaId",
                table: "FloorFeatures",
                column: "DiningAreaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FloorAreas");

            migrationBuilder.DropTable(
                name: "FloorFeatures");

            migrationBuilder.DropTable(
                name: "TablePlacements");
        }
    }
}
