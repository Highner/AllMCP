using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class winevintage2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bottles_Wines_WineId",
                table: "Bottles");

            migrationBuilder.DropColumn(
                name: "Vintage",
                table: "Bottles");

            migrationBuilder.RenameColumn(
                name: "WineId",
                table: "Bottles",
                newName: "WineVintageId");

            migrationBuilder.RenameIndex(
                name: "IX_Bottles_WineId",
                table: "Bottles",
                newName: "IX_Bottles_WineVintageId");

            migrationBuilder.CreateTable(
                name: "WineVintages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Vintage = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WineVintages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WineVintages_Wines_WineId",
                        column: x => x.WineId,
                        principalTable: "Wines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WineVintages_WineId_Vintage",
                table: "WineVintages",
                columns: new[] { "WineId", "Vintage" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Bottles_WineVintages_WineVintageId",
                table: "Bottles",
                column: "WineVintageId",
                principalTable: "WineVintages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bottles_WineVintages_WineVintageId",
                table: "Bottles");

            migrationBuilder.DropTable(
                name: "WineVintages");

            migrationBuilder.RenameColumn(
                name: "WineVintageId",
                table: "Bottles",
                newName: "WineId");

            migrationBuilder.RenameIndex(
                name: "IX_Bottles_WineVintageId",
                table: "Bottles",
                newName: "IX_Bottles_WineId");

            migrationBuilder.AddColumn<int>(
                name: "Vintage",
                table: "Bottles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddForeignKey(
                name: "FK_Bottles_Wines_WineId",
                table: "Bottles",
                column: "WineId",
                principalTable: "Wines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
