using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PbSqlServerMonitoring.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionPlanHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockingHistory");

            migrationBuilder.DropTable(
                name: "QueryHistory");

            migrationBuilder.DropTable(
                name: "MetricSnapshots");

            migrationBuilder.CreateTable(
                name: "ExecutionPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueryHash = table.Column<string>(type: "nvarchar(66)", maxLength: 66, nullable: false),
                    ConnectionId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CompressedPlanXml = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    OriginalSizeBytes = table.Column<int>(type: "int", nullable: false),
                    QueryText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionPlans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionPlans_CapturedAt",
                table: "ExecutionPlans",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionPlans_QueryHash",
                table: "ExecutionPlans",
                column: "QueryHash");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionPlans_QueryHash_ConnectionId",
                table: "ExecutionPlans",
                columns: new[] { "QueryHash", "ConnectionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionPlans");

            migrationBuilder.CreateTable(
                name: "MetricSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActiveConnections = table.Column<int>(type: "int", nullable: false),
                    BlockedProcesses = table.Column<int>(type: "int", nullable: false),
                    BufferCacheHitRatio = table.Column<double>(type: "float", nullable: false),
                    ConnectionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CpuPercent = table.Column<double>(type: "float", nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MemoryMb = table.Column<double>(type: "float", nullable: false),
                    ServerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlockingHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SnapshotId = table.Column<long>(type: "bigint", nullable: false),
                    BlockingSessionId = table.Column<int>(type: "int", nullable: true),
                    ExecutionPlan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsLeadBlocker = table.Column<bool>(type: "bit", nullable: false),
                    QueryText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    WaitTimeMs = table.Column<long>(type: "bigint", nullable: false),
                    WaitType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockingHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockingHistory_MetricSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "MetricSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QueryHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SnapshotId = table.Column<long>(type: "bigint", nullable: false),
                    AvgCpuTimeMs = table.Column<double>(type: "float", nullable: false),
                    AvgElapsedTimeMs = table.Column<double>(type: "float", nullable: false),
                    AvgLogicalReads = table.Column<long>(type: "bigint", nullable: false),
                    AvgLogicalWrites = table.Column<long>(type: "bigint", nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ExecutionCount = table.Column<long>(type: "bigint", nullable: false),
                    ExecutionPlan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastExecutionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QueryHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    QueryText = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueryHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueryHistory_MetricSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "MetricSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockingHistory_SnapshotId",
                table: "BlockingHistory",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_MetricSnapshots_BlockedProcesses_Filtered",
                table: "MetricSnapshots",
                column: "BlockedProcesses",
                filter: "[BlockedProcesses] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_MetricSnapshots_Server_Database_Timestamp",
                table: "MetricSnapshots",
                columns: new[] { "ServerName", "DatabaseName", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_MetricSnapshots_Timestamp",
                table: "MetricSnapshots",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_QueryHistory_LastExecutionTime",
                table: "QueryHistory",
                column: "LastExecutionTime");

            migrationBuilder.CreateIndex(
                name: "IX_QueryHistory_SnapshotId",
                table: "QueryHistory",
                column: "SnapshotId");
        }
    }
}
