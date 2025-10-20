using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class subappellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Wines_Appellations_AppellationId",
                table: "Wines");

            migrationBuilder.RenameColumn(
                name: "AppellationId",
                table: "Wines",
                newName: "SubAppellationId");

            migrationBuilder.RenameIndex(
                name: "IX_Wines_Name_AppellationId",
                table: "Wines",
                newName: "IX_Wines_Name_SubAppellationId");

            migrationBuilder.RenameIndex(
                name: "IX_Wines_AppellationId",
                table: "Wines",
                newName: "IX_Wines_SubAppellationId");

            migrationBuilder.CreateTable(
                name: "SubAppellations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AppellationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubAppellations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubAppellations_Appellations_AppellationId",
                        column: x => x.AppellationId,
                        principalTable: "Appellations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubAppellations_AppellationId",
                table: "SubAppellations",
                column: "AppellationId");

            migrationBuilder.CreateIndex(
                name: "IX_SubAppellations_Name_AppellationId",
                table: "SubAppellations",
                columns: new[] { "Name", "AppellationId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Wines_SubAppellations_SubAppellationId",
                table: "Wines",
                column: "SubAppellationId",
                principalTable: "SubAppellations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Wines_SubAppellations_SubAppellationId",
                table: "Wines");

            migrationBuilder.DropTable(
                name: "SubAppellations");

            migrationBuilder.RenameColumn(
                name: "SubAppellationId",
                table: "Wines",
                newName: "AppellationId");

            migrationBuilder.RenameIndex(
                name: "IX_Wines_SubAppellationId",
                table: "Wines",
                newName: "IX_Wines_AppellationId");

            migrationBuilder.RenameIndex(
                name: "IX_Wines_Name_SubAppellationId",
                table: "Wines",
                newName: "IX_Wines_Name_AppellationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Wines_Appellations_AppellationId",
                table: "Wines",
                column: "AppellationId",
                principalTable: "Appellations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
