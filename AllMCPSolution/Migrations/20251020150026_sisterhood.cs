using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class sisterhood : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sisterhoods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sisterhoods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSisterhoods",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SisterhoodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSisterhoods", x => new { x.UserId, x.SisterhoodId });
                    table.ForeignKey(
                        name: "FK_UserSisterhoods_Sisterhoods_SisterhoodId",
                        column: x => x.SisterhoodId,
                        principalTable: "Sisterhoods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSisterhoods_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sisterhoods_Name",
                table: "Sisterhoods",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSisterhoods_SisterhoodId_UserId",
                table: "UserSisterhoods",
                columns: new[] { "SisterhoodId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSisterhoods");

            migrationBuilder.DropTable(
                name: "Sisterhoods");
        }
    }
}
