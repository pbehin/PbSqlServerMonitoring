using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PbSqlServerMonitoring.Migrations
{
    /// <inheritdoc />
    public partial class AddQueryHistoryLastExecutionTimeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_QueryHistory_LastExecutionTime",
                table: "QueryHistory",
                column: "LastExecutionTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QueryHistory_LastExecutionTime",
                table: "QueryHistory");
        }
    }
}
