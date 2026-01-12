using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Detects missing indexes in SQL Server.
/// 
/// Features:
/// - Analyzes missing index DMVs
/// - Calculates improvement scores
/// - Generates CREATE INDEX statements
/// </summary>
public sealed class MissingIndexService : BaseMonitoringService
{
    #region SQL Queries
    
    private const string MissingIndexQuery = @"
        SELECT TOP (@TopN)
            ISNULL(DB_NAME(mid.database_id), 'Unknown') AS DatabaseName,
            ISNULL(OBJECT_SCHEMA_NAME(mid.object_id, mid.database_id), 'dbo') AS SchemaName,
            ISNULL(OBJECT_NAME(mid.object_id, mid.database_id), 'Unknown') AS TableName,
            ISNULL(mid.equality_columns, '') AS EqualityColumns,
            ISNULL(mid.inequality_columns, '') AS InequalityColumns,
            ISNULL(mid.included_columns, '') AS IncludedColumns,
            ISNULL(migs.user_seeks, 0) * ISNULL(migs.avg_total_user_cost, 0) 
                * (ISNULL(migs.avg_user_impact, 0) / 100.0) AS ImprovementMeasure,
            ISNULL(migs.user_seeks, 0) AS UserSeeks,
            ISNULL(migs.user_scans, 0) AS UserScans,
            ISNULL(migs.avg_total_user_cost, 0) AS AvgTotalUserCost,
            ISNULL(migs.avg_user_impact, 0) AS AvgUserImpact
        FROM sys.dm_db_missing_index_details mid WITH (NOLOCK)
        INNER JOIN sys.dm_db_missing_index_groups mig WITH (NOLOCK)
            ON mid.index_handle = mig.index_handle
        INNER JOIN sys.dm_db_missing_index_group_stats migs WITH (NOLOCK)
            ON mig.index_group_handle = migs.group_handle
        WHERE mid.database_id = DB_ID()
        ORDER BY ImprovementMeasure DESC";
    
    #endregion

    #region Constructor
    
    public MissingIndexService(
        ConnectionService connectionService,
        ILogger<MissingIndexService> logger)
        : base(connectionService, logger)
    {
    }
    
    #endregion

    #region Public Methods
    
    /// <summary>
    /// Gets missing index recommendations sorted by improvement score.
    /// </summary>
    /// <param name="topN">Number of results (max 100)</param>
    public Task<List<MissingIndex>> GetMissingIndexesAsync(int topN = 50)
    {
        return ExecuteMonitoringQueryAsync(
            MissingIndexQuery,
            MapMissingIndex,
            cmd => cmd.Parameters.AddWithValue("@TopN", Math.Clamp(topN, 1, MetricsConstants.MaxTopN)));
    }
    
    #endregion

    #region Private Methods
    
    private static MissingIndex MapMissingIndex(Microsoft.Data.SqlClient.SqlDataReader reader)
    {
        var index = new MissingIndex
        {
            DatabaseName = ReadString(reader, 0, "Unknown"),
            SchemaName = ReadString(reader, 1, "dbo"),
            TableName = ReadString(reader, 2, "Unknown"),
            EqualityColumns = ReadString(reader, 3),
            InequalityColumns = ReadString(reader, 4),
            IncludedColumns = ReadString(reader, 5),
            ImprovementMeasure = ReadDouble(reader, 6),
            UserSeeks = ReadInt64(reader, 7),
            UserScans = ReadInt64(reader, 8),
            AvgTotalUserCost = ReadDouble(reader, 9),
            AvgUserImpact = ReadDouble(reader, 10)
        };

        index.CreateIndexStatement = GenerateCreateIndexStatement(index);
        return index;
    }

    /// <summary>
    /// Generates a CREATE INDEX statement for the missing index.
    /// Uses a consistent naming convention.
    /// </summary>
    private static string GenerateCreateIndexStatement(MissingIndex index)
    {
        // Build column list
        var columns = new List<string>();
        
        if (!string.IsNullOrEmpty(index.EqualityColumns))
        {
            columns.Add(index.EqualityColumns);
        }
        
        if (!string.IsNullOrEmpty(index.InequalityColumns))
        {
            columns.Add(index.InequalityColumns);
        }

        if (columns.Count == 0)
        {
            return "-- No columns specified for index";
        }

        var columnList = string.Join(", ", columns);
        
        // Generate a readable index name
        var indexName = $"IX_{index.TableName}_{GenerateShortHash(columnList)}";
        
        // Build INCLUDE clause if needed
        var includeClause = string.IsNullOrEmpty(index.IncludedColumns)
            ? string.Empty
            : $" INCLUDE ({index.IncludedColumns})";

        return $"CREATE NONCLUSTERED INDEX [{indexName}] " +
               $"ON [{index.SchemaName}].[{index.TableName}] ({columnList}){includeClause};";
    }

    /// <summary>
    /// Generates a short hash for unique index naming.
    /// </summary>
    private static string GenerateShortHash(string input)
    {
        var hash = input.GetHashCode();
        return Math.Abs(hash).ToString("X8")[..6];
    }
    
    #endregion
}
