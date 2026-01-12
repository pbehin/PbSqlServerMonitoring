using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Configuration;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// API controller for connection and application settings.
/// 
/// Security:
/// - Never logs or returns passwords
/// - Connection strings are encrypted at rest
/// - All inputs are validated
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class SettingsController : ControllerBase
{
    #region Fields
    
    private readonly ConnectionService _connectionService;
    private readonly ILogger<SettingsController> _logger;
    
    #endregion

    #region Constructor
    
    public SettingsController(
        ConnectionService connectionService,
        ILogger<SettingsController> logger)
    {
        _connectionService = connectionService;
        _logger = logger;
    }
    
    #endregion

    #region Endpoints
    
    /// <summary>
    /// Gets current connection info (without sensitive data).
    /// </summary>
    [HttpGet("connection")]
    public IActionResult GetConnectionInfo()
    {
        var info = _connectionService.GetConnectionInfo();
        
        return Ok(new
        {
            configured = info.IsConfigured,
            server = info.Server,
            database = info.Database,
            useWindowsAuth = info.UseWindowsAuth,
            username = info.Username,
            trustCertificate = info.TrustCertificate,
            timeout = info.Timeout
        });
    }

    /// <summary>
    /// Updates connection settings.
    /// Tests connection before saving.
    /// </summary>
    [HttpPost("connection")]
    public async Task<IActionResult> UpdateConnection([FromBody] ConnectionRequest request)
    {
        // Validate request
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Invalid request" });
        }

        if (string.IsNullOrWhiteSpace(request.Server))
        {
            return BadRequest(new { success = false, message = "Server name is required" });
        }

        try
        {
            var parameters = MapToParameters(request);
            var connectionString = _connectionService.BuildConnectionString(parameters);

            // Test the connection first
            var testResult = await _connectionService.TestConnectionAsync(connectionString);

            if (!testResult.Success)
            {
                return BadRequest(new
                {
                    success = false,
                    message = testResult.Message
                });
            }

            // Save the connection string (encrypted)
            _connectionService.SetConnectionString(connectionString);

            _logger.LogInformation("Connection updated for server: {Server}", request.Server);

            return Ok(new
            {
                success = true,
                message = "Connection updated successfully",
                serverVersion = testResult.ServerVersion
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update connection");
            return StatusCode(500, new { success = false, message = "An error occurred" });
        }
    }

    /// <summary>
    /// Tests a connection without saving it.
    /// </summary>
    [HttpPost("connection/test")]
    public async Task<IActionResult> TestConnection([FromBody] ConnectionRequest request)
    {
        // Validate request
        if (string.IsNullOrWhiteSpace(request.Server))
        {
            return Ok(new { success = false, message = "Server name is required" });
        }

        try
        {
            var parameters = MapToParameters(request);
            var connectionString = _connectionService.BuildConnectionString(parameters);

            var result = await _connectionService.TestConnectionAsync(connectionString);

            return Ok(new
            {
                success = result.Success,
                message = result.Message,
                serverVersion = result.ServerVersion
            });
        }
        catch (ArgumentException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error testing connection");
            return Ok(new { success = false, message = "Unexpected error testing the connection" });
        }
    }

    /// <summary>
    /// Clears the stored connection.
    /// </summary>
    [HttpDelete("connection")]
    public IActionResult DeleteConnection()
    {
        _connectionService.ClearConnection();
        _logger.LogInformation("Connection cleared by user");
        return Ok(new { success = true, message = "Connection cleared" });
    }
    
    #endregion

    #region Private Methods
    
    private static ConnectionParameters MapToParameters(ConnectionRequest request)
    {
        return new ConnectionParameters
        {
            Server = request.Server ?? string.Empty,
            Database = request.Database ?? "master",
            UseWindowsAuth = request.UseWindowsAuth,
            Username = request.Username,
            Password = request.Password,
            TrustCertificate = request.TrustCertificate,
            Timeout = request.Timeout
        };
    }
    
    #endregion
}

#region Request Models

/// <summary>
/// Connection request model with validation.
/// Note: Password is never logged or returned.
/// </summary>
public sealed class ConnectionRequest
{
    [Required(ErrorMessage = "Server name is required")]
    [StringLength(256, MinimumLength = 1, ErrorMessage = "Server name must be between 1 and 256 characters")]
    public string? Server { get; set; }
    
    [StringLength(128, ErrorMessage = "Database name cannot exceed 128 characters")]
    public string? Database { get; set; } = "master";
    
    [StringLength(128, ErrorMessage = "Username cannot exceed 128 characters")]
    public string? Username { get; set; }
    
    [StringLength(256, ErrorMessage = "Password cannot exceed 256 characters")]
    public string? Password { get; set; }
    
    public bool UseWindowsAuth { get; set; }
    
    /// <summary>
    /// Whether to trust the server certificate.
    /// Null = use environment default (true in dev, false in prod for security)
    /// </summary>
    public bool? TrustCertificate { get; set; }
    
    [Range(MetricsConstants.MinConnectionTimeoutSeconds, MetricsConstants.MaxConnectionTimeoutSeconds, 
        ErrorMessage = "Timeout must be between 5 and 120 seconds")]
    public int Timeout { get; set; } = MetricsConstants.DefaultConnectionTimeoutSeconds;
}

#endregion
