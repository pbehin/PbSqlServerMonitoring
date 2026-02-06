using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PbSqlServerMonitoring.Migrations
{
    /// <inheritdoc />
    public partial class AddEncryptOptionToServerConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Encrypt",
                table: "ServerConnections",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Encrypt",
                table: "ServerConnections");
        }
    }
}
