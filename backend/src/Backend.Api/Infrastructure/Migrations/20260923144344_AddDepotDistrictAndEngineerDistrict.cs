using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepotDistrictAndEngineerDistrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "WorkOrders",
                type: "text",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Priority",
                table: "WorkOrders",
                type: "text",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<Guid>(
                name: "DepotId",
                table: "WorkOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepotId",
                table: "Engineers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "Engineers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Depots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Location_RawAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Location_Coordinates_Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Location_Coordinates_Longitude = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Depots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_DepotId",
                table: "WorkOrders",
                column: "DepotId");

            migrationBuilder.CreateIndex(
                name: "IX_Engineers_DepotId",
                table: "Engineers",
                column: "DepotId");

            migrationBuilder.CreateIndex(
                name: "IX_Engineers_District",
                table: "Engineers",
                column: "District");

            migrationBuilder.CreateIndex(
                name: "IX_Depots_District",
                table: "Depots",
                column: "District",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Engineers_Depots_DepotId",
                table: "Engineers",
                column: "DepotId",
                principalTable: "Depots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_Depots_DepotId",
                table: "WorkOrders",
                column: "DepotId",
                principalTable: "Depots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Engineers_Depots_DepotId",
                table: "Engineers");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Depots_DepotId",
                table: "WorkOrders");

            migrationBuilder.DropTable(
                name: "Depots");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_DepotId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_Engineers_DepotId",
                table: "Engineers");

            migrationBuilder.DropIndex(
                name: "IX_Engineers_District",
                table: "Engineers");

            migrationBuilder.DropColumn(
                name: "DepotId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "DepotId",
                table: "Engineers");

            migrationBuilder.DropColumn(
                name: "District",
                table: "Engineers");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "WorkOrders",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<int>(
                name: "Priority",
                table: "WorkOrders",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
