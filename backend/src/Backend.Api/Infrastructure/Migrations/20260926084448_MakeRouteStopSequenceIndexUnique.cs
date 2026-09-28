using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeRouteStopSequenceIndexUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RouteStops_EngineerRouteId_Sequence",
                table: "RouteStops");

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_EngineerRouteId_Sequence",
                table: "RouteStops",
                columns: new[] { "EngineerRouteId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RouteStops_EngineerRouteId_Sequence",
                table: "RouteStops");

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_EngineerRouteId_Sequence",
                table: "RouteStops",
                columns: new[] { "EngineerRouteId", "Sequence" });
        }
    }
}
