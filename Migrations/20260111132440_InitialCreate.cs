using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PbSqlServerMonitoring.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MetricSnapshots')
                BEGIN
                    CREATE TABLE MetricSnapshots (
                        Id bigint IDENTITY(1,1) NOT NULL,
                        Timestamp datetime2 NOT NULL,
                        ServerName nvarchar(128) NOT NULL,
                        DatabaseName nvarchar(128) NOT NULL,
                        CpuPercent float NOT NULL,
                        MemoryMb float NOT NULL,
                        ActiveConnections int NOT NULL,
                        BlockedProcesses int NOT NULL,
                        BufferCacheHitRatio float NOT NULL,
                        CONSTRAINT PK_MetricSnapshots PRIMARY KEY (Id)
                    );
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BlockingHistory')
                BEGIN
                    CREATE TABLE BlockingHistory (
                        Id bigint IDENTITY(1,1) NOT NULL,
                        SnapshotId bigint NOT NULL,
                        SessionId int NOT NULL,
                        BlockingSessionId int NULL,
                        QueryText nvarchar(500) NULL,
                        WaitTimeMs bigint NOT NULL,
                        WaitType nvarchar(64) NULL,
                        IsLeadBlocker bit NOT NULL,
                        CONSTRAINT PK_BlockingHistory PRIMARY KEY (Id),
                        CONSTRAINT FK_BlockingHistory_MetricSnapshots_SnapshotId FOREIGN KEY (SnapshotId) REFERENCES MetricSnapshots (Id) ON DELETE CASCADE
                    );
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'QueryHistory')
                BEGIN
                    CREATE TABLE QueryHistory (
                        Id bigint IDENTITY(1,1) NOT NULL,
                        SnapshotId bigint NOT NULL,
                        QueryHash nvarchar(64) NULL,
                        QueryText nvarchar(500) NULL,
                        DatabaseName nvarchar(128) NULL,
                        AvgCpuTimeMs float NOT NULL,
                        ExecutionCount bigint NOT NULL,
                        LastExecutionTime datetime2 NULL,
                        AvgLogicalReads bigint NOT NULL,
                        AvgLogicalWrites bigint NOT NULL,
                        AvgElapsedTimeMs float NOT NULL,
                        CONSTRAINT PK_QueryHistory PRIMARY KEY (Id),
                        CONSTRAINT FK_QueryHistory_MetricSnapshots_SnapshotId FOREIGN KEY (SnapshotId) REFERENCES MetricSnapshots (Id) ON DELETE CASCADE
                    );
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_BlockingHistory_SnapshotId' AND object_id = OBJECT_ID('BlockingHistory'))
                BEGIN
                    CREATE INDEX IX_BlockingHistory_SnapshotId ON BlockingHistory (SnapshotId);
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MetricSnapshots_ServerName_DatabaseName' AND object_id = OBJECT_ID('MetricSnapshots'))
                BEGIN
                    CREATE INDEX IX_MetricSnapshots_ServerName_DatabaseName ON MetricSnapshots (ServerName, DatabaseName);
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MetricSnapshots_Timestamp' AND object_id = OBJECT_ID('MetricSnapshots'))
                BEGIN
                    CREATE INDEX IX_MetricSnapshots_Timestamp ON MetricSnapshots (Timestamp);
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_QueryHistory_SnapshotId' AND object_id = OBJECT_ID('QueryHistory'))
                BEGIN
                    CREATE INDEX IX_QueryHistory_SnapshotId ON QueryHistory (SnapshotId);
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockingHistory");

            migrationBuilder.DropTable(
                name: "QueryHistory");

            migrationBuilder.DropTable(
                name: "MetricSnapshots");
        }
    }
}
