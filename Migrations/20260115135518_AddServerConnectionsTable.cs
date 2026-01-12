using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PbSqlServerMonitoring.Migrations
{
    /// <inheritdoc />
    public partial class AddServerConnectionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerConnections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Server = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Database = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UseWindowsAuth = table.Column<bool>(type: "bit", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EncryptedConnectionString = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TrustCertificate = table.Column<bool>(type: "bit", nullable: false),
                    Timeout = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSuccessfulConnection = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequiresReauthenticationSince = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerConnections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerConnections_Status",
                table: "ServerConnections",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ServerConnections_UserId",
                table: "ServerConnections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerConnections_UserId_IsEnabled",
                table: "ServerConnections",
                columns: new[] { "UserId", "IsEnabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerConnections");
        }
    }
}
