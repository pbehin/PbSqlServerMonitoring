using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Services;
using System.Dynamic;

namespace PbSqlServerMonitoring.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly MetricsBufferService _bufferService;
    private readonly IMultiConnectionService _connectionService;

    public DiagnosticsController(
        MetricsBufferService bufferService,
        IMultiConnectionService connectionService)
    {
        _bufferService = bufferService;
        _connectionService = connectionService;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var bufferHealth = _bufferService.GetBufferHealth();
        var connections = _connectionService.GetStatus();

        return Ok(new
        {
            ServerTimeUtc = DateTime.UtcNow,
            LastCollectionTimeUtc = _bufferService.LastCollectionTimeUtc,
            SecondsSinceLastCollection = _bufferService.LastCollectionTimeUtc.HasValue 
                ? (DateTime.UtcNow - _bufferService.LastCollectionTimeUtc.Value).TotalSeconds 
                : (double?)null,
            
            LastError = _bufferService.LastError,
            
            Buffer = new
            {
                PendingCount = bufferHealth.PendingQueueLength,
                RecentCount = bufferHealth.RecentQueueLength,
                DroppedCount = bufferHealth.DroppedPendingTotal,
                LastDropUtc = bufferHealth.LastDropUtc
            },
            
            Connections = new
            {
                Total = connections.ActiveConnections,
                Healthy = connections.HealthyConnections,
                Failed = connections.FailedConnections,
                Warning = connections.ActiveConnections == 0 ? "No connections configured!" : null
            }
        });
    }
}
