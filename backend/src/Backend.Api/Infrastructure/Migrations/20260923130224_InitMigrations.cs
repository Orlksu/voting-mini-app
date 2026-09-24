using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitMigrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Engineers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Transport = table.Column<int>(type: "integer", nullable: false),
                    Skills = table.Column<string>(type: "jsonb", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    MaxOrdersPerShift = table.Column<int>(type: "integer", nullable: false),
                    Shift_End = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Shift_Start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartLocation_RawAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    StartLocation_Coordinates_Latitude = table.Column<double>(type: "double precision", nullable: false),
                    StartLocation_Coordinates_Longitude = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Engineers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Metrics_AssignedOrders = table.Column<int>(type: "integer", nullable: false),
                    Metrics_BaselineActiveEngineers = table.Column<int>(type: "integer", nullable: false),
                    Metrics_BaselineTotalDistanceKm = table.Column<double>(type: "double precision", nullable: false),
                    Metrics_DistanceEconomyPercent = table.Column<double>(type: "double precision", nullable: false),
                    Metrics_OptimizedActiveEngineers = table.Column<int>(type: "integer", nullable: false),
                    Metrics_OptimizedTotalDistanceKm = table.Column<double>(type: "double precision", nullable: false),
                    Metrics_SavedEngineersCount = table.Column<int>(type: "integer", nullable: false),
                    Metrics_TotalOrders = table.Column<int>(type: "integer", nullable: false),
                    Metrics_UnassignedOrders = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ServiceDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    RequiredSkill = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequiredTransport = table.Column<int>(type: "integer", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TaskType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Location_RawAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Location_Coordinates_Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Location_Coordinates_Longitude = table.Column<double>(type: "double precision", nullable: false),
                    PreferredWindow_End = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PreferredWindow_Start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EngineerRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    EngineerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EngineerExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EngineerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Transport = table.Column<int>(type: "integer", nullable: false),
                    BaselineDistanceKm = table.Column<double>(type: "double precision", nullable: true),
                    EncodedPolyline = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineerRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EngineerRoutes_OperationResults_OperationResultId",
                        column: x => x.OperationResultId,
                        principalTable: "OperationResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UnassignedOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReasonCode = table.Column<int>(type: "integer", nullable: false),
                    ReasonDescription = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnassignedOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnassignedOrders_OperationResults_OperationResultId",
                        column: x => x.OperationResultId,
                        principalTable: "OperationResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RouteStops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EngineerRouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    ExternalOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EstimatedArrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ServiceStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ServiceEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DistanceFromPreviousKm = table.Column<double>(type: "double precision", nullable: false),
                    TravelTimeFromPrevious = table.Column<TimeSpan>(type: "interval", nullable: false),
                    WaitingDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    AssignmentReason = table.Column<string>(type: "text", nullable: false),
                    Location_RawAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Location_Coordinates_Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Location_Coordinates_Longitude = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RouteStops_EngineerRoutes_EngineerRouteId",
                        column: x => x.EngineerRouteId,
                        principalTable: "EngineerRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EngineerRoutes_OperationResultId",
                table: "EngineerRoutes",
                column: "OperationResultId");

            migrationBuilder.CreateIndex(
                name: "IX_Engineers_ExternalId",
                table: "Engineers",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_EngineerRouteId_Sequence",
                table: "RouteStops",
                columns: new[] { "EngineerRouteId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_ExternalOrderId",
                table: "RouteStops",
                column: "ExternalOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_UnassignedOrders_ExternalOrderId",
                table: "UnassignedOrders",
                column: "ExternalOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_UnassignedOrders_OperationResultId",
                table: "UnassignedOrders",
                column: "OperationResultId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ExternalId_District",
                table: "WorkOrders",
                columns: new[] { "ExternalId", "District" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Engineers");

            migrationBuilder.DropTable(
                name: "RouteStops");

            migrationBuilder.DropTable(
                name: "UnassignedOrders");

            migrationBuilder.DropTable(
                name: "WorkOrders");

            migrationBuilder.DropTable(
                name: "EngineerRoutes");

            migrationBuilder.DropTable(
                name: "OperationResults");
        }
    }
}
