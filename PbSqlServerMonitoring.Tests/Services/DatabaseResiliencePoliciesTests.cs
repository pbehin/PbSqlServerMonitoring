using Microsoft.Data.SqlClient;
using Polly;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Tests.Services;

/// <summary>
/// Unit tests for DatabaseResiliencePolicies
/// </summary>
public class DatabaseResiliencePoliciesTests
{

    [Fact]
    public void IsTransientError_WithNullException_ReturnsFalse()
    {
        var result = DatabaseResiliencePolicies.IsTransientError(null!);

        Assert.False(result);
    }



    [Fact]
    public void CreateDbRetryPipeline_ReturnsNonNullPipeline()
    {
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline<int>();

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void CreateDbRetryPipeline_Void_ReturnsNonNullPipeline()
    {
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline();

        Assert.NotNull(pipeline);
    }

    [Fact]
    public async Task CreateDbRetryPipeline_ExecutesSuccessfulOperation()
    {
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline<int>();
        var executionCount = 0;

        var result = await pipeline.ExecuteAsync(async ct =>
        {
            executionCount++;
            await Task.Delay(1);
            return 42;
        });

        Assert.Equal(42, result);
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task CreateDbRetryPipeline_RetriesOnTimeoutException()
    {
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline<int>(maxRetries: 3);
        var executionCount = 0;

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

        Assert.Equal(42, result);
        Assert.Equal(3, executionCount);
    }

    [Fact]
    public async Task CreateDbRetryPipeline_ExhaustsRetriesAndThrows()
    {
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline<int>(maxRetries: 2);
        var executionCount = 0;

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await pipeline.ExecuteAsync<int>(ct =>
            {
                executionCount++;
                throw new TimeoutException("Persistent timeout");
            });
        });

        Assert.Equal(3, executionCount);
    }

    [Fact]
    public async Task CreateDbRetryPipeline_VoidVersion_RetriesOnException()
    {
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline(maxRetries: 2);
        var executionCount = 0;

        await pipeline.ExecuteAsync(async ct =>
        {
            executionCount++;
            if (executionCount < 2)
            {
                throw new TimeoutException("Simulated timeout");
            }
            await Task.Delay(1);
        });

        Assert.Equal(2, executionCount);
    }

    [Fact]
    public async Task CreateDbRetryPipeline_DoesNotRetryNonTransientException()
    {
        var pipeline = DatabaseResiliencePolicies.CreateDbRetryPipeline<int>(maxRetries: 3);
        var executionCount = 0;

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await pipeline.ExecuteAsync<int>(ct =>
            {
                executionCount++;
                throw new ArgumentException("This is not transient");
            });
        });

        Assert.Equal(1, executionCount);
    }

}
