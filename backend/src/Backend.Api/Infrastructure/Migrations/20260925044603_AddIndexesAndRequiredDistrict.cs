using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexesAndRequiredDistrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "District",
                table: "WorkOrders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_Status",
                table: "WorkOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_Status_DepotId",
                table: "WorkOrders",
                columns: new[] { "Status", "DepotId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_Status_District",
                table: "WorkOrders",
                columns: new[] { "Status", "District" });

            migrationBuilder.CreateIndex(
                name: "IX_EngineerRoutes_EngineerId",
                table: "EngineerRoutes",
                column: "EngineerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_Status",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_Status_DepotId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_Status_District",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_EngineerRoutes_EngineerId",
                table: "EngineerRoutes");

            migrationBuilder.AlterColumn<string>(
                name: "District",
                table: "WorkOrders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
