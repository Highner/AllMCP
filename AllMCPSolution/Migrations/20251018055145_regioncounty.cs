using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class regioncounty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Wines_Countries_CountryId",
                table: "Wines");

            migrationBuilder.DropIndex(
                name: "IX_Wines_CountryId",
                table: "Wines");

            migrationBuilder.DropIndex(
                name: "IX_Wines_Name_CountryId_RegionId",
                table: "Wines");

            migrationBuilder.DropIndex(
                name: "IX_Regions_Name",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "CountryId",
                table: "Wines");

            migrationBuilder.AddColumn<Guid>(
                name: "CountryId",
                table: "Regions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Wines_Name_RegionId",
                table: "Wines",
                columns: new[] { "Name", "RegionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_CountryId",
                table: "Regions",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Name_CountryId",
                table: "Regions",
                columns: new[] { "Name", "CountryId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Regions_Countries_CountryId",
                table: "Regions",
                column: "CountryId",
                principalTable: "Countries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Regions_Countries_CountryId",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Wines_Name_RegionId",
                table: "Wines");

            migrationBuilder.DropIndex(
                name: "IX_Regions_CountryId",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Regions_Name_CountryId",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "CountryId",
                table: "Regions");

            migrationBuilder.AddColumn<Guid>(
                name: "CountryId",
                table: "Wines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Wines_CountryId",
                table: "Wines",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Wines_Name_CountryId_RegionId",
                table: "Wines",
                columns: new[] { "Name", "CountryId", "RegionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Name",
                table: "Regions",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Wines_Countries_CountryId",
                table: "Wines",
                column: "CountryId",
                principalTable: "Countries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
