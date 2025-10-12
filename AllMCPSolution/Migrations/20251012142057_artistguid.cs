using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllMCPSolution.Migrations
{
    /// <inheritdoc />
    public partial class artistguid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "Artists",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_Artists",
                table: "Artists",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Artists",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "Artists");
        }
    }
}
