using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Tests.Models;

/// <summary>
/// Unit tests for DTO models
/// </summary>
public class ModelTests
{
    [Fact]
    public void MetricDataPoint_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var dataPoint = new MetricDataPoint();

        // Assert
        Assert.Equal("", dataPoint.ServerName);
        Assert.Equal("", dataPoint.DatabaseName);
        Assert.Equal(0, dataPoint.CpuPercent);
        Assert.Equal(0, dataPoint.MemoryMb);
        Assert.Equal(0, dataPoint.ActiveConnections);
        Assert.Equal(0, dataPoint.BlockedProcesses);
        Assert.NotNull(dataPoint.TopQueries);
        Assert.Empty(dataPoint.TopQueries);
        Assert.NotNull(dataPoint.BlockedQueries);
        Assert.Empty(dataPoint.BlockedQueries);
    }

    [Fact]
    public void QuerySnapshot_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var snapshot = new QuerySnapshot();

        // Assert
        Assert.Equal("", snapshot.QueryHash);
        Assert.Equal("", snapshot.QueryTextPreview);
        Assert.Equal(0, snapshot.AvgCpuTimeMs);
        Assert.Equal(0, snapshot.ExecutionCount);
    }

    [Fact]
    public void BlockingSnapshot_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var snapshot = new BlockingSnapshot();

        // Assert
        Assert.Equal(0, snapshot.SessionId);
        Assert.Null(snapshot.BlockingSessionId);
        Assert.Equal("", snapshot.QueryTextPreview);
        Assert.False(snapshot.IsLeadBlocker);
    }

    [Fact]
    public void BufferHealth_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var health = new BufferHealth();

        // Assert
        Assert.Equal(0, health.PendingQueueLength);
        Assert.Equal(0, health.RecentQueueLength);
        Assert.Equal(0, health.DroppedPendingTotal);
        Assert.Null(health.LastDropUtc);
    }
}
