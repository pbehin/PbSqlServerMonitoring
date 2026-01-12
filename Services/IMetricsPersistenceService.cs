using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Services;

public interface IMetricsPersistenceService
{
    Task EnsureDatabaseAsync(CancellationToken cancellationToken = default);
    Task SaveMetricsAsync(IEnumerable<MetricDataPoint> metrics, string connectionId);
    Task<List<MetricDataPoint>> GetMetricsAsync(int rangeSeconds, string connectionId);

    Task<List<MetricDataPoint>> GetMetricsByDateRangeAsync(DateTime from, DateTime to, string connectionId);
    Task<List<QuerySnapshot>> GetQueryHistoryAsync(int rangeSeconds, string connectionId);
    Task<string?> GetExecutionPlanAsync(string connectionId, string queryHash);

    Task<PagedResult<MetricDataPoint>> GetMetricsPagedAsync(int rangeSeconds, string connectionId, int skip, int take, bool blockedOnly = false, bool includeBlockingDetails = false);

    Task<PagedResult<MetricDataPoint>> GetMetricsByDateRangePagedAsync(DateTime from, DateTime to, string connectionId, int skip, int take, bool blockedOnly = false, bool includeBlockingDetails = false);
}
