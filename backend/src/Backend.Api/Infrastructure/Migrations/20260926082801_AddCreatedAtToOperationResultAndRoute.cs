using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedAtToOperationResultAndRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "OperationResults",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "EngineerRoutes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.CreateIndex(
                name: "IX_OperationResults_CreatedAt",
                table: "OperationResults",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EngineerRoutes_CreatedAt",
                table: "EngineerRoutes",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OperationResults_CreatedAt",
                table: "OperationResults");

            migrationBuilder.DropIndex(
                name: "IX_EngineerRoutes_CreatedAt",
                table: "EngineerRoutes");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "OperationResults");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "EngineerRoutes");
        }
    }
}
