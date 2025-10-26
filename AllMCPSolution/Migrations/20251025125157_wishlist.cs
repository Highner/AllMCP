using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class wishlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Wishlists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wishlists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wishlists_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WineVintageWishes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WishlistId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WineVintageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WineVintageWishes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WineVintageWishes_WineVintages_WineVintageId",
                        column: x => x.WineVintageId,
                        principalTable: "WineVintages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WineVintageWishes_Wishlists_WishlistId",
                        column: x => x.WishlistId,
                        principalTable: "Wishlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WineVintageWishes_WineVintageId",
                table: "WineVintageWishes",
                column: "WineVintageId");

            migrationBuilder.CreateIndex(
                name: "IX_WineVintageWishes_WishlistId_WineVintageId",
                table: "WineVintageWishes",
                columns: new[] { "WishlistId", "WineVintageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wishlists_UserId_Name",
                table: "Wishlists",
                columns: new[] { "UserId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WineVintageWishes");

            migrationBuilder.DropTable(
                name: "Wishlists");
        }
    }
}
