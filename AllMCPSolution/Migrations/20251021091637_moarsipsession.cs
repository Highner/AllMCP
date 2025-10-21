using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class moarsipsession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "Date",
                table: "SipSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "SipSessions",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BottleSipSession",
                columns: table => new
                {
                    BottlesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SipSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BottleSipSession", x => new { x.BottlesId, x.SipSessionId });
                    table.ForeignKey(
                        name: "FK_BottleSipSession_Bottles_BottlesId",
                        column: x => x.BottlesId,
                        principalTable: "Bottles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BottleSipSession_SipSessions_SipSessionId",
                        column: x => x.SipSessionId,
                        principalTable: "SipSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BottleSipSession_SipSessionId",
                table: "BottleSipSession",
                column: "SipSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BottleSipSession");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "SipSessions");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "SipSessions");
        }
    }
}
