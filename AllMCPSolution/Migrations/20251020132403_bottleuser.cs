using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class bottleuser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BottleLocations_Name",
                table: "BottleLocations");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Bottles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "BottleLocations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Bottles_UserId",
                table: "Bottles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BottleLocations_UserId_Name",
                table: "BottleLocations",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BottleLocations_Users_UserId",
                table: "BottleLocations",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bottles_Users_UserId",
                table: "Bottles",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BottleLocations_Users_UserId",
                table: "BottleLocations");

            migrationBuilder.DropForeignKey(
                name: "FK_Bottles_Users_UserId",
                table: "Bottles");

            migrationBuilder.DropIndex(
                name: "IX_Bottles_UserId",
                table: "Bottles");

            migrationBuilder.DropIndex(
                name: "IX_BottleLocations_UserId_Name",
                table: "BottleLocations");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Bottles");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "BottleLocations");

            migrationBuilder.CreateIndex(
                name: "IX_BottleLocations_Name",
                table: "BottleLocations",
                column: "Name",
                unique: true);
        }
    }
}
