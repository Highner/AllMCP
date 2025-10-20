using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class bottlelocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubAppellations_Name_AppellationId",
                table: "SubAppellations");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "SubAppellations",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AddColumn<Guid>(
                name: "BottleLocationId",
                table: "Bottles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BottleLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BottleLocations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubAppellations_Name_AppellationId",
                table: "SubAppellations",
                columns: new[] { "Name", "AppellationId" },
                unique: true,
                filter: "[Name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bottles_BottleLocationId",
                table: "Bottles",
                column: "BottleLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_BottleLocations_Name",
                table: "BottleLocations",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Bottles_BottleLocations_BottleLocationId",
                table: "Bottles",
                column: "BottleLocationId",
                principalTable: "BottleLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bottles_BottleLocations_BottleLocationId",
                table: "Bottles");

            migrationBuilder.DropTable(
                name: "BottleLocations");

            migrationBuilder.DropIndex(
                name: "IX_SubAppellations_Name_AppellationId",
                table: "SubAppellations");

            migrationBuilder.DropIndex(
                name: "IX_Bottles_BottleLocationId",
                table: "Bottles");

            migrationBuilder.DropColumn(
                name: "BottleLocationId",
                table: "Bottles");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "SubAppellations",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubAppellations_Name_AppellationId",
                table: "SubAppellations",
                columns: new[] { "Name", "AppellationId" },
                unique: true);
        }
    }
}
