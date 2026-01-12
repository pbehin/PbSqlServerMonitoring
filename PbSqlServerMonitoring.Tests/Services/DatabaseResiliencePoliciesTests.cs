using Microsoft.Data.SqlClient;
using Polly;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Tests.Services;

/// <summary>
/// Unit tests for DatabaseResiliencePolicies
/// </summary>
public class DatabaseResiliencePoliciesTests
{
    #region IsTransientError Tests - Using actual null check only
    
    [Fact]
    public void IsTransientError_WithNullException_ReturnsFalse()
    {
        // Act
        var result = DatabaseResiliencePolicies.IsTransientError(null!);
        
        // Assert
        Assert.False(result);
    }
    
    #endregion
    
    #region CreateDbRetryPipeline Tests
    
    [Fact]
    public void CreateDbRetryPipeline_ReturnsNonNullPipeline()
    {
        // Act
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline<int>();
        
        // Assert
        Assert.NotNull(pipeline);
    }
    
    [Fact]
    public void CreateDbRetryPipeline_Void_ReturnsNonNullPipeline()
    {
        // Act
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline();
        
        // Assert
        Assert.NotNull(pipeline);
    }
    
    [Fact]
    public async Task CreateDbRetryPipeline_ExecutesSuccessfulOperation()
    {
        // Arrange
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline<int>();
        var executionCount = 0;
        
        // Act
        var result = await pipeline.ExecuteAsync(async ct =>
        {
            executionCount++;
            await Task.Delay(1);
            return 42;
        });
        
        // Assert
        Assert.Equal(42, result);
        Assert.Equal(1, executionCount);
    }
    
    [Fact]
    public async Task CreateDbRetryPipeline_RetriesOnTimeoutException()
    {
        // Arrange
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline<int>(maxRetries: 3);
        var executionCount = 0;
        
        // Act
        var result = await pipeline.ExecuteAsync(async ct =>
        {
            executionCount++;
            if (executionCount < 3)
            {
                throw new TimeoutException("Simulated timeout");
            }
            await Task.Delay(1);
            return 42;
        });
        
        // Assert
        Assert.Equal(42, result);
        Assert.Equal(3, executionCount); // Should have retried twice
    }
    
    [Fact]
    public async Task CreateDbRetryPipeline_ExhaustsRetriesAndThrows()
    {
        // Arrange
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline<int>(maxRetries: 2);
        var executionCount = 0;
        
        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await pipeline.ExecuteAsync<int>(ct =>
            {
                executionCount++;
                throw new TimeoutException("Persistent timeout");
            });
        });
        
        Assert.Equal(3, executionCount); // Initial + 2 retries
    }
    
    [Fact]
    public async Task CreateDbRetryPipeline_VoidVersion_RetriesOnException()
    {
        // Arrange
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline(maxRetries: 2);
        var executionCount = 0;
        
        // Act
        await pipeline.ExecuteAsync(async ct =>
        {
            executionCount++;
            if (executionCount < 2)
            {
                throw new TimeoutException("Simulated timeout");
            }
            await Task.Delay(1);
        });
        
        // Assert
        Assert.Equal(2, executionCount);
    }
    
    [Fact]
    public async Task CreateDbRetryPipeline_DoesNotRetryNonTransientException()
    {
        // Arrange - ArgumentException is not a transient error
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline<int>(maxRetries: 3);
        var executionCount = 0;
        
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await pipeline.ExecuteAsync<int>(ct =>
            {
                executionCount++;
                throw new ArgumentException("This is not transient");
            });
        });
        
        // Should only execute once - no retries for non-transient exceptions
        Assert.Equal(1, executionCount);
    }
    
    #endregion
}
