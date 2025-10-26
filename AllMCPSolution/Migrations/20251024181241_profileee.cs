using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class profileee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SuggestedAppellations_AspNetUsers_UserId",
                table: "SuggestedAppellations");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "SuggestedAppellations",
                newName: "TasteProfileId");

            migrationBuilder.RenameIndex(
                name: "IX_SuggestedAppellations_UserId_SubAppellationId",
                table: "SuggestedAppellations",
                newName: "IX_SuggestedAppellations_TasteProfileId_SubAppellationId");

            migrationBuilder.AddForeignKey(
                name: "FK_SuggestedAppellations_TasteProfiles_TasteProfileId",
                table: "SuggestedAppellations",
                column: "TasteProfileId",
                principalTable: "TasteProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SuggestedAppellations_TasteProfiles_TasteProfileId",
                table: "SuggestedAppellations");

            migrationBuilder.RenameColumn(
                name: "TasteProfileId",
                table: "SuggestedAppellations",
                newName: "UserId");

            migrationBuilder.RenameIndex(
                name: "IX_SuggestedAppellations_TasteProfileId_SubAppellationId",
                table: "SuggestedAppellations",
                newName: "IX_SuggestedAppellations_UserId_SubAppellationId");

            migrationBuilder.AddForeignKey(
                name: "FK_SuggestedAppellations_AspNetUsers_UserId",
                table: "SuggestedAppellations",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
