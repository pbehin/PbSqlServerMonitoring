using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PbSqlServerMonitoring.Migrations
{
    /// <inheritdoc />
    public partial class EnsureQueryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('QueryHistory') AND name = 'AvgLogicalReads')
                BEGIN
                    ALTER TABLE QueryHistory ADD AvgLogicalReads bigint NOT NULL DEFAULT 0;
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('QueryHistory') AND name = 'AvgLogicalWrites')
                BEGIN
                    ALTER TABLE QueryHistory ADD AvgLogicalWrites bigint NOT NULL DEFAULT 0;
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('QueryHistory') AND name = 'AvgElapsedTimeMs')
                BEGIN
                    ALTER TABLE QueryHistory ADD AvgElapsedTimeMs float NOT NULL DEFAULT 0;
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('QueryHistory') AND name = 'LastExecutionTime')
                BEGIN
                    ALTER TABLE QueryHistory ADD LastExecutionTime datetime2 NULL;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
