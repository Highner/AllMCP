using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class appellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Wines_Regions_RegionId",
                table: "Wines");

            migrationBuilder.RenameColumn(
                name: "RegionId",
                table: "Wines",
                newName: "AppellationId");

            migrationBuilder.RenameIndex(
                name: "IX_Wines_RegionId",
                table: "Wines",
                newName: "IX_Wines_AppellationId");

            migrationBuilder.RenameIndex(
                name: "IX_Wines_Name_RegionId",
                table: "Wines",
                newName: "IX_Wines_Name_AppellationId");

            migrationBuilder.CreateTable(
                name: "Appellations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RegionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appellations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Appellations_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appellations_Name_RegionId",
                table: "Appellations",
                columns: new[] { "Name", "RegionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appellations_RegionId",
                table: "Appellations",
                column: "RegionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Wines_Appellations_AppellationId",
                table: "Wines",
                column: "AppellationId",
                principalTable: "Appellations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Wines_Appellations_AppellationId",
                table: "Wines");

            migrationBuilder.DropTable(
                name: "Appellations");

            migrationBuilder.RenameColumn(
                name: "AppellationId",
                table: "Wines",
                newName: "RegionId");

            migrationBuilder.RenameIndex(
                name: "IX_Wines_Name_AppellationId",
                table: "Wines",
                newName: "IX_Wines_Name_RegionId");

            migrationBuilder.RenameIndex(
                name: "IX_Wines_AppellationId",
                table: "Wines",
                newName: "IX_Wines_RegionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Wines_Regions_RegionId",
                table: "Wines",
                column: "RegionId",
                principalTable: "Regions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
