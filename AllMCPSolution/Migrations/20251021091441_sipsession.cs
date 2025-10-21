using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class sipsession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SipSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SisterhoodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SipSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SipSessions_Sisterhoods_SisterhoodId",
                        column: x => x.SisterhoodId,
                        principalTable: "Sisterhoods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SipSessions_SisterhoodId_ScheduledAt",
                table: "SipSessions",
                columns: new[] { "SisterhoodId", "ScheduledAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SipSessions");
        }
    }
}
