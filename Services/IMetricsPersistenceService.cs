using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

public interface IMetricsPersistenceService
{
    Task EnsureDatabaseAsync();
    Task SaveMetricsAsync(IEnumerable<MetricDataPoint> metrics, string serverName, string databaseName);
    Task<List<MetricDataPoint>> GetMetricsAsync(int rangeSeconds, string serverName, string databaseName);

    Task<List<MetricDataPoint>> GetMetricsByDateRangeAsync(DateTime from, DateTime to, string serverName, string databaseName);
    Task<List<QuerySnapshot>> GetQueryHistoryAsync(int rangeSeconds, string serverName, string databaseName);

    Task<PagedResult<MetricDataPoint>> GetMetricsPagedAsync(int rangeSeconds, string serverName, string databaseName, int skip, int take, bool blockedOnly = false, bool includeBlockingDetails = false);

    Task<PagedResult<MetricDataPoint>> GetMetricsByDateRangePagedAsync(DateTime from, DateTime to, string serverName, string databaseName, int skip, int take, bool blockedOnly = false, bool includeBlockingDetails = false);
}
