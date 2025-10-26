using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class notasteuser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Artworks");

            migrationBuilder.DropTable(
                name: "ArtworkSales");

            migrationBuilder.DropTable(
                name: "InflationIndices");

            migrationBuilder.DropTable(
                name: "Artists");

            migrationBuilder.DropColumn(
                name: "TasteProfile",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TasteProfileSummary",
                table: "AspNetUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TasteProfile",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TasteProfileSummary",
                table: "AspNetUsers",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InflationIndices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IndexValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InflationIndices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Artworks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArtistId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    YearCreated = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artworks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Artworks_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArtworkSales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArtistId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HammerPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Height = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    HighEstimate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LowEstimate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SaleDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Sold = table.Column<bool>(type: "bit", nullable: false),
                    Technique = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Width = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    YearCreated = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtworkSales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArtworkSales_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Artworks_ArtistId",
                table: "Artworks",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtworkSales_ArtistId",
                table: "ArtworkSales",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtworkSales_Name_Height_Width_HammerPrice_SaleDate_ArtistId",
                table: "ArtworkSales",
                columns: new[] { "Name", "Height", "Width", "HammerPrice", "SaleDate", "ArtistId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InflationIndices_Year_Month",
                table: "InflationIndices",
                columns: new[] { "Year", "Month" },
                unique: true);
        }
    }
}
