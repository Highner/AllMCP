using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class suggestedappellations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SuggestedAppellations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubAppellationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuggestedAppellations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuggestedAppellations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SuggestedAppellations_SubAppellations_SubAppellationId",
                        column: x => x.SubAppellationId,
                        principalTable: "SubAppellations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SuggestedAppellations_SubAppellationId",
                table: "SuggestedAppellations",
                column: "SubAppellationId");

            migrationBuilder.CreateIndex(
                name: "IX_SuggestedAppellations_UserId_SubAppellationId",
                table: "SuggestedAppellations",
                columns: new[] { "UserId", "SubAppellationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SuggestedAppellations");
        }
    }
}
