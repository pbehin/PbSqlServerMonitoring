using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PbSqlServerMonitoring.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectionIdToMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MetricSnapshots_ServerName_DatabaseName",
                table: "MetricSnapshots");

            migrationBuilder.AddColumn<string>(
                name: "ConnectionId",
                table: "MetricSnapshots",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_MetricSnapshots_BlockedProcesses_Filtered",
                table: "MetricSnapshots",
                column: "BlockedProcesses",
                filter: "[BlockedProcesses] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_MetricSnapshots_Server_Database_Timestamp",
                table: "MetricSnapshots",
                columns: new[] { "ServerName", "DatabaseName", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MetricSnapshots_BlockedProcesses_Filtered",
                table: "MetricSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_MetricSnapshots_Server_Database_Timestamp",
                table: "MetricSnapshots");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "MetricSnapshots");

            migrationBuilder.CreateIndex(
                name: "IX_MetricSnapshots_ServerName_DatabaseName",
                table: "MetricSnapshots",
                columns: new[] { "ServerName", "DatabaseName" });
        }
    }
}
