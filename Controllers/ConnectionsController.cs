using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// API controller for managing multiple SQL Server connections.
/// 
/// Features:
/// - Add/remove connections (up to configured limit)
/// - Enable/disable individual connections
/// - Test connections
/// - Get status of all connections
/// 
/// Security:
/// - All endpoints require authorization
/// - Passwords are never returned in responses
/// - Connection strings encrypted at rest
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class ConnectionsController : ControllerBase
{
    private readonly MultiConnectionService _connectionService;
    private readonly UserPreferencesService _userPreferencesService;
    private readonly ILogger<ConnectionsController> _logger;

    public ConnectionsController(
        MultiConnectionService connectionService,
        UserPreferencesService userPreferencesService,
        ILogger<ConnectionsController> logger)
    {
        _connectionService = connectionService;
        _userPreferencesService = userPreferencesService;
        _logger = logger;
    }

    #region Endpoints
    
    /// <summary>
    /// Gets the status of all configured connections.
    /// </summary>
    /// <returns>Overview of all connections including health status</returns>
    [HttpGet]
    [ProducesResponseType(typeof(MultiConnectionStatusResponse), StatusCodes.Status200OK)]
    public IActionResult GetAllConnections()
    {
        var userId = GetUserIdentifier();
        var status = _connectionService.GetStatus();
        
        // Filter to only show connections owned by this user
        status.Connections = status.Connections
            .Where(c => _connectionService.GetConnection(c.Id)?.UserId == userId)
            .ToList();
        status.ActiveConnections = status.Connections.Count;
        status.HealthyConnections = status.Connections.Count(c => c.Status == ConnectionStatus.Connected);
        status.FailedConnections = status.Connections.Count(c => c.Status == ConnectionStatus.Error || c.Status == ConnectionStatus.Disconnected);
        
        return Ok(status);
    }

    /// <summary>
    /// Gets a specific connection by ID.
    /// </summary>
    /// <param name="id">Connection ID</param>
    /// <returns>Connection details (without sensitive data)</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ConnectionInfoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetConnection(string id)
    {
        var userId = GetUserIdentifier();
        var connection = _connectionService.GetConnection(id);
        
        if (connection == null || connection.UserId != userId)
        {
            return NotFound(new { success = false, message = "Connection not found." });
        }

        return Ok(MapToResponse(connection));
    }

    /// <summary>
    /// Adds a new connection.
    /// Tests the connection before saving.
    /// </summary>
    /// <param name="request">Connection parameters</param>
    /// <returns>The created connection info</returns>
    [HttpPost]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddConnection([FromBody] AddConnectionRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Invalid request." });
        }

        var userId = GetUserIdentifier();
        var result = await _connectionService.AddConnectionAsync(request, userId);

        if (!result.Success)
        {
            return BadRequest(new { success = false, message = result.Message });
        }
        
        // Connection is strongly typed now with UserId persisted by service
        _logger.LogInformation("Connection added for user {UserId}: {ConnectionName} ({Server})", 
            userId, result.Connection!.Name, result.Connection.Server);

        return Created(
            $"/api/connections/{result.Connection.Id}", 
            new 
            { 
                success = true, 
                message = result.Message,
                connection = MapToResponse(result.Connection)
            });
    }

    /// <summary>
    /// Removes a connection.
    /// </summary>
    /// <param name="id">Connection ID to remove</param>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveConnection(string id)
    {
        var userId = GetUserIdentifier();
        var connection = _connectionService.GetConnection(id);
        
        // Verify ownership
        if (connection == null || connection.UserId != userId)
        {
            return NotFound(new { success = false, message = "Connection not found." });
        }
        
        var result = await _connectionService.RemoveConnectionAsync(id);

        if (!result.Success)
        {
            return NotFound(new { success = false, message = result.Message });
        }

        _logger.LogInformation("Connection removed by user {UserId}: {ConnectionId}", userId, id);

        return Ok(new { success = true, message = result.Message });
    }

    /// <summary>
    /// Enables or disables a connection.
    /// </summary>
    /// <param name="id">Connection ID</param>
    /// <param name="request">Enable status</param>
    [HttpPatch("{id}/enable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetConnectionEnabled(string id, [FromBody] EnableConnectionRequest request)
    {
        var userId = GetUserIdentifier();
        var connection = _connectionService.GetConnection(id);
        
        // Verify ownership
        if (connection == null || connection.UserId != userId)
        {
            return NotFound(new { success = false, message = "Connection not found." });
        }
        
        var result = await _connectionService.SetConnectionEnabledAsync(id, request.Enabled);

        if (!result.Success)
        {
            return NotFound(new { success = false, message = result.Message });
        }

        return Ok(new { success = true, message = result.Message });
    }

    /// <summary>
    /// Tests a specific stored connection.
    /// </summary>
    /// <param name="id">Connection ID to test</param>
    [HttpPost("{id}/test")]
    [EnableRateLimiting("connection-test")] // Stricter rate limit to prevent brute-force
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestConnection(string id)
    {
        var userId = GetUserIdentifier();
        var connection = _connectionService.GetConnection(id);
        
        // Verify ownership
        if (connection == null || connection.UserId != userId)
        {
            return NotFound(new { success = false, message = "Connection not found." });
        }
        
        var result = await _connectionService.TestStoredConnectionAsync(id);

        if (!result.Success)
        {
            return Ok(new { success = false, message = result.Message });
        }

        return Ok(new { success = true, message = result.Message });
    }

    /// <summary>
    /// Disconnects a connection - stops data collection.
    /// </summary>
    /// <param name="id">Connection ID to disconnect</param>
    [HttpPost("{id}/disconnect")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisconnectConnection(string id)
    {
        var userId = GetUserIdentifier();
        var connection = _connectionService.GetConnection(id);
        
        // Verify ownership
        if (connection == null || connection.UserId != userId)
        {
            return NotFound(new { success = false, message = "Connection not found." });
        }
        
        var result = await _connectionService.DisconnectConnectionAsync(id);

        if (!result.Success)
        {
            return Ok(new { success = false, message = result.Message });
        }

        _logger.LogInformation("Connection disconnected by user {UserId}: {ConnectionId}", userId, id);

        return Ok(new { success = true, message = result.Message });
    }

    /// <summary>
    /// Tests all enabled connections.
    /// </summary>
    [HttpPost("test-all")]
    [EnableRateLimiting("connection-test")] // Stricter rate limit to prevent abuse
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> TestAllConnections()
    {
        await _connectionService.TestAllConnectionsAsync();
        
        var status = _connectionService.GetStatus();
        
        return Ok(new 
        { 
            success = true, 
            message = $"Tested {status.ActiveConnections} connections. {status.HealthyConnections} healthy, {status.FailedConnections} failed.",
            status
        });
    }

    /// <summary>
    /// Gets connection limits and current usage.
    /// </summary>
    [HttpGet("limits")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetConnectionLimits()
    {
        return Ok(new
        {
            maxConnections = _connectionService.MaxConnections,
            currentConnections = _connectionService.ConnectionCount,
            available = _connectionService.MaxConnections - _connectionService.ConnectionCount
        });
    }

    /// <summary>
    /// Gets the user's active connection ID.
    /// </summary>
    [HttpGet("active")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetActiveConnection()
    {
        var userId = GetUserIdentifier();
        var activeId = _userPreferencesService.GetActiveConnectionId(userId);
        
        // Validate the connection still exists and is enabled
        if (!string.IsNullOrEmpty(activeId))
        {
            var connection = _connectionService.GetConnection(activeId);
            if (connection == null || !connection.IsEnabled)
            {
                // Clear invalid active connection
                _userPreferencesService.SetActiveConnectionId(userId, null);
                activeId = null;
            }
        }
        
        return Ok(new { activeConnectionId = activeId });
    }
    
    /// <summary>
    /// Sets the user's active connection ID.
    /// </summary>
    [HttpPut("active")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult SetActiveConnection([FromBody] SetActiveConnectionRequest request)
    {
        var userId = GetUserIdentifier();
        
        // Validate the connection exists and is enabled
        if (!string.IsNullOrEmpty(request.ConnectionId))
        {
            var connection = _connectionService.GetConnection(request.ConnectionId);
            if (connection == null)
            {
                return BadRequest(new { success = false, message = "Connection not found." });
            }
            if (!connection.IsEnabled)
            {
                return BadRequest(new { success = false, message = "Connection is disabled." });
            }
        }
        
        _userPreferencesService.SetActiveConnectionId(userId, request.ConnectionId);
        
        _logger.LogInformation("User set active connection to {ConnectionId}", request.ConnectionId ?? "(none)");
        
        return Ok(new { success = true, activeConnectionId = request.ConnectionId });
    }

    #endregion

    #region Helpers

    private static ConnectionInfoResponse MapToResponse(ServerConnection connection)
    {
        return new ConnectionInfoResponse
        {
            Id = connection.Id,
            Name = connection.Name,
            Server = connection.Server,
            Database = connection.Database,
            UseWindowsAuth = connection.UseWindowsAuth,
            Username = connection.Username,
            TrustCertificate = connection.TrustCertificate,
            Timeout = connection.Timeout,
            IsEnabled = connection.IsEnabled,
            CreatedAt = connection.CreatedAt,
            LastSuccessfulConnection = connection.LastSuccessfulConnection,
            Status = connection.Status,
            LastError = connection.LastError
        };
    }
    
    /// <summary>
    /// Gets the current user's ID from ASP.NET Core Identity.
    /// </summary>
    private string GetUserIdentifier()
    {
        // Get user ID from Identity claims (set by ASP.NET Core Identity)
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        
        if (string.IsNullOrEmpty(userId))
        {
            // Fallback for edge cases (should not happen with [Authorize])
            _logger.LogWarning("User ID not found in claims");
            return "anonymous";
        }
        
        return userId;
    }

    #endregion
}

/// <summary>
/// Request for enabling/disabling a connection.
/// </summary>
public sealed class EnableConnectionRequest
{
    public bool Enabled { get; set; }
}

/// <summary>
/// Request for setting the active connection.
/// </summary>
public sealed class SetActiveConnectionRequest
{
    public string? ConnectionId { get; set; }
}
