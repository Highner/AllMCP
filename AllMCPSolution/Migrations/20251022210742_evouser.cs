using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class evouser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WineVintageEvolutionScores_WineVintageId_Year",
                table: "WineVintageEvolutionScores");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "WineVintageEvolutionScores",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_WineVintageEvolutionScores_UserId_WineVintageId_Year",
                table: "WineVintageEvolutionScores",
                columns: new[] { "UserId", "WineVintageId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WineVintageEvolutionScores_WineVintageId",
                table: "WineVintageEvolutionScores",
                column: "WineVintageId");

            migrationBuilder.AddForeignKey(
                name: "FK_WineVintageEvolutionScores_AspNetUsers_UserId",
                table: "WineVintageEvolutionScores",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WineVintageEvolutionScores_AspNetUsers_UserId",
                table: "WineVintageEvolutionScores");

            migrationBuilder.DropIndex(
                name: "IX_WineVintageEvolutionScores_UserId_WineVintageId_Year",
                table: "WineVintageEvolutionScores");

            migrationBuilder.DropIndex(
                name: "IX_WineVintageEvolutionScores_WineVintageId",
                table: "WineVintageEvolutionScores");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "WineVintageEvolutionScores");

            migrationBuilder.CreateIndex(
                name: "IX_WineVintageEvolutionScores_WineVintageId_Year",
                table: "WineVintageEvolutionScores",
                columns: new[] { "WineVintageId", "Year" },
                unique: true);
        }
    }
}
