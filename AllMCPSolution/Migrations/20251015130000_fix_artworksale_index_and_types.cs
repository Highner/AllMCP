using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

using AllMCPSolution.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AllMCPSolution.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20251015130000_fix_artworksale_index_and_types")]
    public partial class fix_artworksale_index_and_types : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create InflationIndices table if not exists
            migrationBuilder.CreateTable(
                name: "InflationIndices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    IndexValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InflationIndices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InflationIndices_Year_Month",
                table: "InflationIndices",
                columns: new[] { "Year", "Month" },
                unique: true);

            // Align ArtworkSales schema
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ArtworkSales",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Height",
                table: "ArtworkSales",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Width",
                table: "ArtworkSales",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            // Drop existing composite index if present (guarded)
            migrationBuilder.Sql(@"
IF EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_ArtworkSales_Name_Height_Width_HammerPrice_SaleDate_ArtistId' AND object_id = OBJECT_ID('dbo.ArtworkSales'))
BEGIN
    DROP INDEX [IX_ArtworkSales_Name_Height_Width_HammerPrice_SaleDate_ArtistId] ON [dbo].[ArtworkSales];
END");

            // Remove duplicate rows that would violate the unique composite index
            migrationBuilder.Sql(@"
;WITH D AS (
    SELECT [Id], ROW_NUMBER() OVER (
        PARTITION BY [Name], [Height], [Width], [HammerPrice], [SaleDate], [ArtistId]
        ORDER BY [Id]
    ) AS rn
    FROM [dbo].[ArtworkSales]
)
DELETE FROM [dbo].[ArtworkSales]
WHERE [Id] IN (SELECT [Id] FROM D WHERE rn > 1);
");

            migrationBuilder.CreateIndex(
                name: "IX_ArtworkSales_Name_Height_Width_HammerPrice_SaleDate_ArtistId",
                table: "ArtworkSales",
                columns: new[] { "Name", "Height", "Width", "HammerPrice", "SaleDate", "ArtistId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the composite unique index
            migrationBuilder.Sql(@"
IF EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_ArtworkSales_Name_Height_Width_HammerPrice_SaleDate_ArtistId' AND object_id = OBJECT_ID('dbo.ArtworkSales'))
BEGIN
    DROP INDEX [IX_ArtworkSales_Name_Height_Width_HammerPrice_SaleDate_ArtistId] ON [dbo].[ArtworkSales];
END");

            migrationBuilder.AlterColumn<decimal>(
                name: "Width",
                table: "ArtworkSales",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Height",
                table: "ArtworkSales",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ArtworkSales",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            // Drop InflationIndices
            migrationBuilder.DropTable(
                name: "InflationIndices");
        }
    }
}
