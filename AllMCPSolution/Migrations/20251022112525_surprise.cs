using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class surprise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BottleSipSession_Bottles_BottlesId",
                table: "BottleSipSession");

            migrationBuilder.RenameColumn(
                name: "BottlesId",
                table: "BottleSipSession",
                newName: "BottleId");

            migrationBuilder.AddColumn<bool>(
                name: "IsRevealed",
                table: "BottleSipSession",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_BottleSipSession_Bottles_BottleId",
                table: "BottleSipSession",
                column: "BottleId",
                principalTable: "Bottles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BottleSipSession_Bottles_BottleId",
                table: "BottleSipSession");

            migrationBuilder.DropColumn(
                name: "IsRevealed",
                table: "BottleSipSession");

            migrationBuilder.RenameColumn(
                name: "BottleId",
                table: "BottleSipSession",
                newName: "BottlesId");

            migrationBuilder.AddForeignKey(
                name: "FK_BottleSipSession_Bottles_BottlesId",
                table: "BottleSipSession",
                column: "BottlesId",
                principalTable: "Bottles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
