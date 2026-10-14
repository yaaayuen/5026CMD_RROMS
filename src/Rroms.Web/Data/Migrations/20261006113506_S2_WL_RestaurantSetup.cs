using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Rroms.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class S2_WL_RestaurantSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiningAreas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiningAreas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MenuCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperatingHours",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DayOfWeek = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    ClosesAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatingHours", x => x.Id);
                    table.CheckConstraint("CK_OperatingHours_OpensBeforeCloses", "[IsClosed] = 1 OR [OpensAt] < [ClosesAt]");
                });

            migrationBuilder.CreateTable(
                name: "RestaurantProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpecialClosures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecialClosures", x => x.Id);
                    table.CheckConstraint("CK_SpecialClosures_EndNotBeforeStart", "[EndDate] >= [StartDate]");
                });

            migrationBuilder.CreateTable(
                name: "DiningTables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Label = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    DiningAreaId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiningTables", x => x.Id);
                    table.CheckConstraint("CK_DiningTables_Capacity", "[Capacity] BETWEEN 1 AND 20");
                    table.ForeignKey(
                        name: "FK_DiningTables_DiningAreas_DiningAreaId",
                        column: x => x.DiningAreaId,
                        principalTable: "DiningAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "DiningAreas",
                columns: new[] { "Id", "Description", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "Indoor, air-conditioned", true, "Main Hall" },
                    { 2, "Outdoor, covered", true, "Patio" }
                });

            migrationBuilder.InsertData(
                table: "MenuCategories",
                columns: new[] { "Id", "DisplayOrder", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, 10, true, "Starters" },
                    { 2, 20, true, "Grills" },
                    { 3, 30, true, "Pasta" },
                    { 4, 40, true, "Desserts" },
                    { 5, 50, true, "Beverages" }
                });

            migrationBuilder.InsertData(
                table: "OperatingHours",
                columns: new[] { "Id", "ClosesAt", "DayOfWeek", "IsClosed", "OpensAt" },
                values: new object[,]
                {
                    { 1, new TimeOnly(22, 0, 0), "Monday", false, new TimeOnly(11, 0, 0) },
                    { 2, new TimeOnly(22, 0, 0), "Tuesday", false, new TimeOnly(11, 0, 0) },
                    { 3, new TimeOnly(22, 0, 0), "Wednesday", false, new TimeOnly(11, 0, 0) },
                    { 4, new TimeOnly(22, 0, 0), "Thursday", false, new TimeOnly(11, 0, 0) },
                    { 5, new TimeOnly(22, 0, 0), "Friday", false, new TimeOnly(11, 0, 0) },
                    { 6, new TimeOnly(22, 0, 0), "Saturday", false, new TimeOnly(11, 0, 0) },
                    { 7, new TimeOnly(22, 0, 0), "Sunday", false, new TimeOnly(11, 0, 0) }
                });

            migrationBuilder.InsertData(
                table: "RestaurantProfiles",
                columns: new[] { "Id", "Address", "Email", "Name", "Phone", "UpdatedAtUtc" },
                values: new object[] { 1, "1 Example Street, 50000 Kuala Lumpur (placeholder)", "hello@threegrills.example", "Three Grills", "+60 3-0000 0000", new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "DiningTables",
                columns: new[] { "Id", "Capacity", "DiningAreaId", "IsActive", "Label" },
                values: new object[,]
                {
                    { 1, 2, 1, true, "T01" },
                    { 2, 2, 1, true, "T02" },
                    { 3, 4, 1, true, "T03" },
                    { 4, 4, 1, true, "T04" },
                    { 5, 6, 1, true, "T05" },
                    { 6, 8, 1, true, "T06" },
                    { 7, 2, 2, true, "P01" },
                    { 8, 4, 2, true, "P02" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiningAreas_Name",
                table: "DiningAreas",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiningTables_DiningAreaId",
                table: "DiningTables",
                column: "DiningAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_DiningTables_Label",
                table: "DiningTables",
                column: "Label",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuCategories_Name",
                table: "MenuCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatingHours_DayOfWeek",
                table: "OperatingHours",
                column: "DayOfWeek",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecialClosures_StartDate",
                table: "SpecialClosures",
                column: "StartDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiningTables");

            migrationBuilder.DropTable(
                name: "MenuCategories");

            migrationBuilder.DropTable(
                name: "OperatingHours");

            migrationBuilder.DropTable(
                name: "RestaurantProfiles");

            migrationBuilder.DropTable(
                name: "SpecialClosures");

            migrationBuilder.DropTable(
                name: "DiningAreas");
        }
    }
}
