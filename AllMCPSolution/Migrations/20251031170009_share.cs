using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class share : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotTasted",
                table: "TastingNotes");

            migrationBuilder.CreateTable(
                name: "BottleShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BottleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SharedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SharedWithUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SharedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BottleShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BottleShares_AspNetUsers_SharedByUserId",
                        column: x => x.SharedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BottleShares_AspNetUsers_SharedWithUserId",
                        column: x => x.SharedWithUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BottleShares_Bottles_BottleId",
                        column: x => x.BottleId,
                        principalTable: "Bottles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BottleShares_BottleId_SharedWithUserId",
                table: "BottleShares",
                columns: new[] { "BottleId", "SharedWithUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BottleShares_SharedByUserId",
                table: "BottleShares",
                column: "SharedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BottleShares_SharedWithUserId",
                table: "BottleShares",
                column: "SharedWithUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BottleShares");

            migrationBuilder.AddColumn<bool>(
                name: "NotTasted",
                table: "TastingNotes",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
