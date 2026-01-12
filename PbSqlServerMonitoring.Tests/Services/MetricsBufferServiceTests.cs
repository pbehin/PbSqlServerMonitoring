using Microsoft.Extensions.Logging;
using Moq;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Tests.Services;

/// <summary>
/// Unit tests for MetricsBufferService
/// </summary>
public class MetricsBufferServiceTests
{
    private readonly Mock<ILogger<MetricsBufferService>> _mockLogger;
    private readonly MetricsBufferService _service;

    public MetricsBufferServiceTests()
    {
        _mockLogger = new Mock<ILogger<MetricsBufferService>>();
        _service = new MetricsBufferService(_mockLogger.Object);
    }

    private static MetricDataPoint CreateDataPoint(string connectionId = "test-connection-id", DateTime? timestamp = null)
    {
        return new MetricDataPoint
        {
            ConnectionId = connectionId,
            ServerName = "TestServer",
            DatabaseName = "TestDB",
            Timestamp = timestamp ?? DateTime.UtcNow,
            CpuPercent = 50.0,
            MemoryMb = 1024,
            ActiveConnections = 10,
            BlockedProcesses = 0
        };
    }

    #region Enqueue Tests
    
    [Fact]
    public void Enqueue_AddsToRecentAndPending()
    {
        // Arrange
        var dataPoint = CreateDataPoint();
        
        // Act
        _service.Enqueue(dataPoint);
        
        // Assert
        Assert.False(_service.IsPendingQueueEmpty);
        var recent = _service.GetRecentDataPoints("test-connection-id");
        Assert.Single(recent);
    }
    
    [Fact]
    public void Enqueue_MultipleDataPoints_PreservesOrder()
    {
        // Arrange
        var dp1 = CreateDataPoint(timestamp: DateTime.UtcNow.AddMinutes(-2));
        var dp2 = CreateDataPoint(timestamp: DateTime.UtcNow.AddMinutes(-1));
        var dp3 = CreateDataPoint(timestamp: DateTime.UtcNow);
        
        // Act
        _service.Enqueue(dp1);
        _service.Enqueue(dp2);
        _service.Enqueue(dp3);
        
        // Assert
        var recent = _service.GetRecentDataPoints("test-connection-id").ToList();
        Assert.Equal(3, recent.Count);
        Assert.True(recent[0].Timestamp <= recent[1].Timestamp);
        Assert.True(recent[1].Timestamp <= recent[2].Timestamp);
    }
    
    #endregion
    
    #region GetRecentDataPoints Tests
    
    [Fact]
    public void GetRecentDataPoints_FiltersByConnectionId()
    {
        // Arrange
        _service.Enqueue(CreateDataPoint("conn-1"));
        _service.Enqueue(CreateDataPoint("conn-2"));
        _service.Enqueue(CreateDataPoint("conn-3"));
        
        // Act
        var result = _service.GetRecentDataPoints("conn-1").ToList();
        
        // Assert
        Assert.Single(result);
        Assert.Equal("conn-1", result[0].ConnectionId);
    }
    
    [Fact]
    public void GetRecentDataPoints_WithCutoff_FiltersOldData()
    {
        // Arrange
        var oldPoint = CreateDataPoint(timestamp: DateTime.UtcNow.AddMinutes(-30));
        var newPoint = CreateDataPoint(timestamp: DateTime.UtcNow);
        _service.Enqueue(oldPoint);
        _service.Enqueue(newPoint);
        
        // Act
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var result = _service.GetRecentDataPoints("test-connection-id", cutoff).ToList();
        
        // Assert
        Assert.Single(result);
        Assert.True(result[0].Timestamp >= cutoff);
    }
    
    [Fact]
    public void GetRecentDataPoints_NoMatchingData_ReturnsEmpty()
    {
        // Arrange
        _service.Enqueue(CreateDataPoint("conn-1"));
        
        // Act
        var result = _service.GetRecentDataPoints("non-existent");
        
        // Assert
        Assert.Empty(result);
    }
    
    #endregion
    
    #region GetLatest Tests
    
    [Fact]
    public void GetLatest_ReturnsNewestDataPoint()
    {
        // Arrange
        var oldPoint = CreateDataPoint(timestamp: DateTime.UtcNow.AddMinutes(-5));
        oldPoint.CpuPercent = 25;
        var newPoint = CreateDataPoint(timestamp: DateTime.UtcNow);
        newPoint.CpuPercent = 75;
        
        _service.Enqueue(oldPoint);
        _service.Enqueue(newPoint);
        
        // Act
        var result = _service.GetLatest("test-connection-id");
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(75, result.CpuPercent);
    }
    
    [Fact]
    public void GetLatest_NoData_ReturnsNull()
    {
        // Act
        var result = _service.GetLatest("non-existent");
        
        // Assert
        Assert.Null(result);
    }
    
    #endregion
    
    #region DequeuePendingForSave Tests
    
    [Fact]
    public void DequeuePendingForSave_ReturnsAllPendingItems()
    {
        // Arrange
        _service.Enqueue(CreateDataPoint());
        _service.Enqueue(CreateDataPoint());
        _service.Enqueue(CreateDataPoint());
        
        // Act
        var result = _service.DequeuePendingForSave();
        
        // Assert
        Assert.Equal(3, result.Count);
        Assert.True(_service.IsPendingQueueEmpty);
    }
    
    [Fact]
    public void DequeuePendingForSave_CalledTwice_SecondReturnsEmpty()
    {
        // Arrange
        _service.Enqueue(CreateDataPoint());
        
        // Act
        var first = _service.DequeuePendingForSave();
        var second = _service.DequeuePendingForSave();
        
        // Assert
        Assert.Single(first);
        Assert.Empty(second);
    }
    
    #endregion
    
    #region RequeueFailedPoints Tests
    
    [Fact]
    public void RequeueFailedPoints_AddsBackToPendingQueue()
    {
        // Arrange
        _service.Enqueue(CreateDataPoint());
        var dequeued = _service.DequeuePendingForSave();
        Assert.True(_service.IsPendingQueueEmpty);
        
        // Act
        _service.RequeueFailedPoints(dequeued);
        
        // Assert
        Assert.False(_service.IsPendingQueueEmpty);
    }
    
    #endregion
    
    #region BufferHealth Tests
    
    [Fact]
    public void GetBufferHealth_ReturnsAccurateCounts()
    {
        // Arrange
        _service.Enqueue(CreateDataPoint());
        _service.Enqueue(CreateDataPoint());
        
        // Act
        var health = _service.GetBufferHealth();
        
        // Assert
        Assert.Equal(2, health.PendingQueueLength);
        Assert.Equal(2, health.RecentQueueLength);
        Assert.Equal(0, health.DroppedPendingTotal);
        Assert.Null(health.LastDropUtc);
    }
    
    [Fact]
    public void ShouldThrottleCollection_WhenQueueLow_ReturnsFalse()
    {
        // Arrange
        _service.Enqueue(CreateDataPoint());
        
        // Act & Assert
        Assert.False(_service.ShouldThrottleCollection);
    }
    
    #endregion
    
    #region Cleanup Tests
    
    [Fact]
    public void Cleanup_RemovesOldDataPoints()
    {
        // Arrange
        var oldPoint = CreateDataPoint(timestamp: DateTime.UtcNow.AddMinutes(-20));
        var newPoint = CreateDataPoint(timestamp: DateTime.UtcNow);
        _service.Enqueue(oldPoint);
        _service.Enqueue(newPoint);
        
        // Act
        _service.Cleanup();
        
        // Assert
        var remaining = _service.GetRecentDataPoints("test-connection-id").ToList();
        Assert.Single(remaining);
    }
    
    #endregion
}
