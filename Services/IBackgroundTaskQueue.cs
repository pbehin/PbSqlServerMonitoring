using System.Threading.Channels;

namespace PbSqlServerMonitoring.Services;

/// <summary>
/// Interface for a background task queue that processes work items asynchronously.
/// Replaces fire-and-forget Task.Run patterns with proper lifecycle management.
/// </summary>
public interface IBackgroundTaskQueue
{
    /// <summary>
    /// Queues a work item for background processing.
    /// </summary>
    /// <param name="workItem">The async work item to execute</param>
    void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem);
    
    /// <summary>
    /// Dequeues a work item for processing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The next work item to process</returns>
    Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Thread-safe background task queue implementation using Channels.
/// Provides bounded capacity with backpressure to prevent memory issues.
/// </summary>
public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<CancellationToken, Task>> _queue;
    private readonly ILogger<BackgroundTaskQueue> _logger;
    
    /// <summary>
    /// Maximum number of queued work items before blocking
    /// </summary>
    private const int MaxQueuedItems = 100;
    
    public BackgroundTaskQueue(ILogger<BackgroundTaskQueue> logger)
    {
        _logger = logger;
        
        // Bounded channel with dropping behavior when full
        var options = new BoundedChannelOptions(MaxQueuedItems)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        
        _queue = Channel.CreateBounded<Func<CancellationToken, Task>>(options);
    }
    
    public void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        
        if (!_queue.Writer.TryWrite(workItem))
        {
            _logger.LogWarning("Background task queue is full, work item dropped");
        }
    }
    
    public async Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}

/// <summary>
/// Hosted service that processes background work items from the queue.
/// </summary>
public sealed class BackgroundTaskQueueHostedService : BackgroundService
{
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly ILogger<BackgroundTaskQueueHostedService> _logger;
    
    public BackgroundTaskQueueHostedService(
        IBackgroundTaskQueue taskQueue,
        ILogger<BackgroundTaskQueueHostedService> logger)
    {
        _taskQueue = taskQueue;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background task queue service is starting");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await _taskQueue.DequeueAsync(stoppingToken);
                
                try
                {
                    await workItem(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing background work item");
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }
        
        _logger.LogInformation("Background task queue service is stopping");
    }
}
