using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class suggestedwines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SuggestedWines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuggestedAppellationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Vintage = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuggestedWines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuggestedWines_SuggestedAppellations_SuggestedAppellationId",
                        column: x => x.SuggestedAppellationId,
                        principalTable: "SuggestedAppellations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SuggestedWines_Wines_WineId",
                        column: x => x.WineId,
                        principalTable: "Wines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SuggestedWines_SuggestedAppellationId_WineId",
                table: "SuggestedWines",
                columns: new[] { "SuggestedAppellationId", "WineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SuggestedWines_WineId",
                table: "SuggestedWines",
                column: "WineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SuggestedWines");
        }
    }
}
